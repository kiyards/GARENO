using System;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace ProjectRuntime.Actor
{
    public enum DungeonMasterCardPlacementState
    {
        Idle,
        SelectingPlacement,
        ChargingPlacement,
    }

    public class DungeonMasterCardManager : NetworkBehaviour
    {
        private const int HandSize = 4;
        private const int PlacementIndicatorRingSegments = 96;
        private const float PlacementIndicatorRingInnerRadius = 0.78f;
        private const string PlacementIndicatorShaderName = "Universal Render Pipeline/Unlit";
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        [SerializeField]
        private float manaRegenRate = 1f;

        [SerializeField]
        private int maxMana = 10;

        [Header("Placement")]
        [SerializeField]
        private float placementRayDistance = 1000f;

        [SerializeField]
        private float placementIndicatorRadius = 1.5f;

        [SerializeField]
        private float placementIndicatorHeight = 0.03f;

        [SerializeField]
        private float placementPreviewLift = 0.12f;

        [SerializeField]
        private float placementChargeDuration = 1f;

        [SyncVar(hook = nameof(OnManaChanged))]
        public float Mana;

        public event Action<float, int> OnManaChangedEvent;
        public int MaxMana => this.maxMana;

        [Header("Nemesis")]
        [SerializeField] private float nemesisBaseCountdown = 10f;

        // Server-authoritative network time at which the Nemesis becomes available. Shortened by
        // BattleManager on crystal/downed/kill events via ServerShortenNemesisCountdown.
        [SyncVar(hook = nameof(OnNemesisReadyTimeChanged))]
        private double _nemesisReadyNetworkTime;

        [SyncVar(hook = nameof(OnNemesisAvailableChanged))]
        private bool _nemesisAvailable;

        [SyncVar(hook = nameof(OnNemesisActiveChanged))]
        private bool _nemesisActive;

        // Raised on the owning Dungeon Master when availability/active state flips or the ready
        // time is adjusted. The HUD also polls NemesisRemainingSeconds each frame for the live
        // countdown (the ready-time SyncVar only changes on events, not per frame).
        public event Action OnNemesisAvailabilityChangedEvent;

        public bool NemesisAvailable => this._nemesisAvailable;
        public bool NemesisActive => this._nemesisActive;
        public float NemesisRemainingSeconds =>
            (float)Math.Max(0d, this._nemesisReadyNetworkTime - NetworkTime.time);
        // 0 when the cooldown just started → 1 at READY. Approximated against the base countdown, so
        // event-driven shortening reads as the meter jumping forward. Drives the HUD charge fill.
        public float NemesisReadyProgress =>
            this.nemesisBaseCountdown <= 0f
                ? 1f
                : Mathf.Clamp01(1f - this.NemesisRemainingSeconds / this.nemesisBaseCountdown);

        // Replicated mirror of the server-authoritative _hand so the owning Dungeon Master's HUD
        // can render the 4 cards. Server writes it; clients read it. A null/empty entry marks an
        // empty hand slot (the deck and used pile have run dry).
        public readonly SyncList<string> HandCardIds = new();

        // Raised on every peer when the hand contents change. The hand HUD subscribes to refresh.
        public event Action OnHandChangedEvent;
        public event Action OnPlacementStateChangedEvent;

        [Header("Debug")]
        [SerializeField] private bool _debugForceCard;
        [SerializeField] private string _debugForcedCardId;

        // Server-only deck state. Stores card ids; null/empty marks an empty hand slot.
        private readonly List<string> _hand = new();
        private readonly Queue<string> _deck = new();
        private readonly List<string> _used = new();

        private GameplayPlayer _player;
        private GameplayPlayer Player => this._player ??= this.GetComponent<GameplayPlayer>();

        private GameObject _placementIndicator;
        private Material _placementIndicatorMaterial;
        private Mesh _placementIndicatorRingMesh;
        private DungeonMasterPlacementPreview _placementPreview;
        private int _selectedHandSlot = -1;
        private string _selectedCardId;
        private DungeonMasterCardPlacementState _placementState =
            DungeonMasterCardPlacementState.Idle;
        private bool _hasPlacementPoint;
        private Vector3 _placementPosition;
        private Vector3 _placementNormal = Vector3.up;
        private float _placementChargeStartTime;
        private int _placementStartedFrame = -1;
        private int _committedHandSlot = -1;
        private string _committedCardId;
        // True while the current placement targeting is for the Nemesis side-card (free, no hand slot)
        // rather than a hand card. Distinguishes the two paths in the shared placement flow.
        private bool _nemesisPlacementPending;

        public DungeonMasterCardPlacementState PlacementState => this._placementState;
        public bool IsPlacementModeActive =>
            this._placementState != DungeonMasterCardPlacementState.Idle;
        public bool IsPlacementCharging =>
            this._placementState == DungeonMasterCardPlacementState.ChargingPlacement;
        public string SelectedCardId => this._selectedCardId;
        // True while the active placement targeting is for the Nemesis side-card (no hand card, so
        // SelectedCardId is null). Lets the hand UI label the placement "Nemesis".
        public bool IsNemesisPlacementActive => this._nemesisPlacementPending;
        public float PlacementChargeProgress =>
            this.IsPlacementCharging && this.placementChargeDuration > 0f
                ? Mathf.Clamp01(
                    (Time.time - this._placementChargeStartTime) / this.placementChargeDuration
                )
                : 0f;

        private void Awake()
        {
            this.HandCardIds.OnChange += this.OnHandCardsChanged;
        }

        private void OnDestroy()
        {
            this.HandCardIds.OnChange -= this.OnHandCardsChanged;
            this.DestroyPlacementPreview();
            this.DestroyPlacementIndicator();
        }

        private void OnHandCardsChanged(SyncList<string>.Operation op, int index, string item) =>
            this.OnHandChangedEvent?.Invoke();

        public override void OnStartServer()
        {
            base.OnStartServer();
            this.ServerInitializeDeck();
            this._nemesisReadyNetworkTime = NetworkTime.time + this.nemesisBaseCountdown;
            this._nemesisAvailable = false;
            this._nemesisActive = false;
        }

        private void Update()
        {
            if (this.isServer)
            {
                this.ServerTickMana();
                this.ServerTickNemesisAvailability();
            }

            if (this.isLocalPlayer)
            {
                this.ClientTickPlacement();
            }
        }

        [Server]
        private void ServerTickMana()
        {
            if (!this.Player.IsDungeonMaster)
                return;
            this.Mana = Mathf.Min(this.Mana + this.manaRegenRate * Time.deltaTime, this.maxMana);
        }

        private void OnManaChanged(float oldVal, float newVal) =>
            this.OnManaChangedEvent?.Invoke(newVal, this.maxMana);

        [Server]
        private void ServerTickNemesisAvailability()
        {
            if (!this.Player.IsDungeonMaster) return;
            if (this._nemesisAvailable || this._nemesisActive) return;
            if (NetworkTime.time >= this._nemesisReadyNetworkTime)
            {
                this._nemesisAvailable = true;
            }
        }

        // Called by BattleManager when a crystal is destroyed / survivor downed / survivor killed.
        [Server]
        public void ServerShortenNemesisCountdown(float seconds)
        {
            if (!this.Player.IsDungeonMaster) return;
            if (this._nemesisAvailable || this._nemesisActive || seconds <= 0f) return;

            double floor = NetworkTime.time;
            double shortened = this._nemesisReadyNetworkTime - seconds;
            this._nemesisReadyNetworkTime = shortened < floor ? floor : shortened;

            if (NetworkTime.time >= this._nemesisReadyNetworkTime)
            {
                this._nemesisAvailable = true;
            }
        }

        // Spawns the Nemesis at the placement-confirmed position and locks out the side-card while it is
        // alive. Only flips to "active" once the entity actually spawns. Not one-time-use: when the
        // Nemesis ends the countdown restarts (see ServerOnNemesisEnded).
        [Server]
        public bool ServerTryActivateNemesis(Vector3 position)
        {
            if (!this.Player.IsDungeonMaster) return false;
            if (!this._nemesisAvailable || this._nemesisActive) return false;

            if (!this.Player.Nemesis.ServerSpawnNemesis(position))
            {
                return false;
            }

            this._nemesisActive = true;
            this._nemesisAvailable = false;
            return true;
        }

        // Called when the active Nemesis ends (lifetime expiry, manual disassemble, or destruction) via
        // DungeonMasterNemesisController.DetachSpawnedNemesis. Clears the active flag and restarts the
        // countdown so the Nemesis becomes available again — it is not a one-time-use ability.
        [Server]
        public void ServerOnNemesisEnded()
        {
            if (!this.Player.IsDungeonMaster) return;
            if (!this._nemesisActive) return;

            this._nemesisActive = false;
            this._nemesisAvailable = false;
            this._nemesisReadyNetworkTime = NetworkTime.time + this.nemesisBaseCountdown;
        }

        // Begins placement targeting for the Nemesis side-card. Reuses the card placement flow (green
        // indicator, 1s charge, right-click cancel), but the Nemesis costs no mana and has no hand slot.
        // Called by the HUD when the (available) Nemesis button is clicked.
        public void TryBeginNemesisPlacement()
        {
            if (!this.isLocalPlayer || !this.CanUseLocalPlacement())
            {
                return;
            }

            if (!this._nemesisAvailable || this._nemesisActive)
            {
                return;
            }

            this.CancelPlacement();

            this._nemesisPlacementPending = true;
            this._selectedHandSlot = -1;
            this._selectedCardId = null;
            this._placementState = DungeonMasterCardPlacementState.SelectingPlacement;
            this._placementStartedFrame = Time.frameCount;
            this._hasPlacementPoint = false;
            this.EnsurePlacementIndicator();
            this.ShowNemesisPlacementPreview();
            this.SetPlacementIndicatorVisible(false);
            this.NotifyPlacementStateChanged();
        }

        private void OnNemesisReadyTimeChanged(double oldVal, double newVal)
            => this.OnNemesisAvailabilityChangedEvent?.Invoke();

        private void OnNemesisAvailableChanged(bool oldVal, bool newVal)
            => this.OnNemesisAvailabilityChangedEvent?.Invoke();

        private void OnNemesisActiveChanged(bool oldVal, bool newVal)
            => this.OnNemesisAvailabilityChangedEvent?.Invoke();

        [Server]
        public bool ServerTrySpendMana(int amount)
        {
            if (this.Mana < amount)
                return false;
            this.Mana -= amount;
            return true;
        }

        [Command]
        public void CmdPlayCard(int handSlot, Vector3 groundPosition)
        {
            this.ServerPlayCard(handSlot, groundPosition);
        }

        [Command]
        private void CmdCommitCardCharge(int handSlot, string cardId)
        {
            if (!this.ServerCommitCardCharge(handSlot, cardId))
            {
                this.TargetRejectCardCharge(this.connectionToClient);
            }
        }

        [Command]
        private void CmdPlayCommittedCard(int handSlot, string cardId, Vector3 groundPosition)
        {
            this.ServerPlayCommittedCard(handSlot, cardId, groundPosition);
        }

        [TargetRpc]
        private void TargetRejectCardCharge(NetworkConnectionToClient target)
        {
            if (this._placementState == DungeonMasterCardPlacementState.ChargingPlacement)
            {
                this.CancelPlacement();
            }
        }

        private void ClientTickPlacement()
        {
            if (!this.CanUseLocalPlacement())
            {
                this.CancelPlacement();
                return;
            }

            if (this._placementState == DungeonMasterCardPlacementState.Idle)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (
                this._placementState == DungeonMasterCardPlacementState.SelectingPlacement
                && mouse.rightButton.wasPressedThisFrame
            )
            {
                this.CancelPlacement();
                return;
            }

            if (this._placementState == DungeonMasterCardPlacementState.SelectingPlacement)
            {
                this.UpdatePlacementPoint(mouse);
                if (
                    this._hasPlacementPoint
                    && Time.frameCount != this._placementStartedFrame
                    && mouse.leftButton.wasPressedThisFrame
                    && !IsPointerOverUi()
                )
                {
                    this.BeginPlacementCharge();
                }

                return;
            }

            if (this._placementState == DungeonMasterCardPlacementState.ChargingPlacement)
            {
                this.UpdateChargingIndicator();
                if (Time.time - this._placementChargeStartTime >= this.placementChargeDuration)
                {
                    this.PlayChargedCardAtPlacementPoint();
                }
            }
        }

        public void TryBeginPlacementFromHand(int handSlot)
        {
            if (!this.isLocalPlayer || !this.CanUseLocalPlacement())
            {
                return;
            }

            this.TryBeginPlacement(handSlot);
        }

        private bool CanUseLocalPlacement()
        {
            var player = this.Player;
            return player != null
                && player.IsDungeonMaster
                && player.currentState is DungeonMasterMovementState;
        }

        private void TryBeginPlacement(int handSlot)
        {
            if (handSlot < 0 || handSlot >= this.HandCardIds.Count)
            {
                return;
            }

            var cardId = this.HandCardIds[handSlot];
            var cardData = string.IsNullOrEmpty(cardId) ? null : DCard.GetDataById(cardId);
            if (cardData == null)
            {
                return;
            }

            var card = cardData.Value;
            if (this.Mana < card.ManaCost)
            {
                return;
            }

            this.CancelPlacement();

            this._selectedHandSlot = handSlot;
            this._selectedCardId = cardId;
            this._placementState = DungeonMasterCardPlacementState.SelectingPlacement;
            this._placementStartedFrame = Time.frameCount;
            this._hasPlacementPoint = false;
            this.EnsurePlacementIndicator();
            this.ShowCardPlacementPreview(card.Effect);
            this.SetPlacementIndicatorVisible(false);
            this.NotifyPlacementStateChanged();
        }

        private void UpdatePlacementPoint(Mouse mouse)
        {
            if (!this.TryGetGroundPoint(mouse, out Vector3 position, out Vector3 normal))
            {
                this._hasPlacementPoint = false;
                this.SetPlacementIndicatorVisible(false);
                this.SetPlacementPreviewVisible(false);
                return;
            }

            this._hasPlacementPoint = true;
            this._placementPosition = position;
            this._placementNormal = normal;
            this.SetPlacementIndicator(position, normal);
            this.SetPlacementIndicatorColor(new Color(0.1f, 1f, 0.25f, 0.65f));
            this.SetPlacementIndicatorVisible(true);
            this.SetPlacementPreview(position, normal, true);
        }

        private void BeginPlacementCharge()
        {
            if (!this._hasPlacementPoint)
            {
                return;
            }

            // The Nemesis is free, so there's no mana to commit — only hand cards commit a charge.
            if (!this._nemesisPlacementPending)
            {
                this.CmdCommitCardCharge(this._selectedHandSlot, this._selectedCardId);
            }

            this._placementState = DungeonMasterCardPlacementState.ChargingPlacement;
            this._placementChargeStartTime = Time.time;
            this.UpdateChargingIndicator();
            this.NotifyPlacementStateChanged();
        }

        private void UpdateChargingIndicator()
        {
            if (this._placementIndicator == null)
            {
                return;
            }

            float progress = this.PlacementChargeProgress;
            float pulse = 1f + Mathf.Sin(Time.time * 18f) * 0.08f;
            Vector3 safeNormal =
                this._placementNormal.sqrMagnitude > 0.0001f
                    ? this._placementNormal.normalized
                    : Vector3.up;

            this._placementIndicator.transform.SetPositionAndRotation(
                this._placementPosition + safeNormal * this.placementIndicatorHeight,
                Quaternion.FromToRotation(Vector3.up, safeNormal)
            );
            this._placementIndicator.transform.localScale = new Vector3(
                this.placementIndicatorRadius * pulse,
                1f,
                this.placementIndicatorRadius * pulse
            );
            this.SetPlacementPreview(this._placementPosition, safeNormal, true);
            this.SetPlacementIndicatorColor(
                Color.Lerp(
                    new Color(0.1f, 1f, 0.25f, 0.65f),
                    new Color(0.75f, 1f, 0.2f, 0.85f),
                    progress
                )
            );
            this.SetPlacementIndicatorVisible(true);
        }

        private void PlayChargedCardAtPlacementPoint()
        {
            if (this._nemesisPlacementPending)
            {
                Vector3 nemesisPosition = this._placementPosition;
                this.CancelPlacement();
                this.Player.CmdActivateNemesisAt(nemesisPosition);
                return;
            }

            int handSlot = this._selectedHandSlot;
            string cardId = this._selectedCardId;
            Vector3 groundPosition = this._placementPosition;
            this.CancelPlacement();
            this.CmdPlayCommittedCard(handSlot, cardId, groundPosition);
        }

        private void CancelPlacement()
        {
            if (
                this._placementState == DungeonMasterCardPlacementState.Idle
                && this._selectedHandSlot < 0
                && string.IsNullOrEmpty(this._selectedCardId)
                && !this._nemesisPlacementPending
            )
            {
                return;
            }

            this._nemesisPlacementPending = false;
            this._selectedHandSlot = -1;
            this._selectedCardId = null;
            this._placementState = DungeonMasterCardPlacementState.Idle;
            this._placementStartedFrame = -1;
            this._hasPlacementPoint = false;
            this.SetPlacementIndicatorVisible(false);
            this.DestroyPlacementPreview();
            this.NotifyPlacementStateChanged();
        }

        private bool TryGetGroundPoint(Mouse mouse, out Vector3 position, out Vector3 normal)
        {
            position = Vector3.zero;
            normal = Vector3.up;

            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0)
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
            if (
                !Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    this.placementRayDistance,
                    1 << groundLayer,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                return false;
            }

            position = hit.point;
            normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal : Vector3.up;
            return true;
        }

        private void EnsurePlacementIndicator()
        {
            if (this._placementIndicator != null)
            {
                return;
            }

            this._placementIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            this._placementIndicator.name = "DungeonMasterPlacementIndicator";

            if (this._placementIndicator.TryGetComponent(out Collider indicatorCollider))
            {
                Destroy(indicatorCollider);
            }

            if (this._placementIndicator.TryGetComponent(out Renderer indicatorRenderer))
            {
                this._placementIndicatorMaterial =
                    CreatePlacementIndicatorMaterial(indicatorRenderer.sharedMaterial);
                indicatorRenderer.material = this._placementIndicatorMaterial;
                indicatorRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                indicatorRenderer.receiveShadows = false;
            }

            if (this._placementIndicator.TryGetComponent(out MeshFilter indicatorMeshFilter))
            {
                this._placementIndicatorRingMesh = CreatePlacementIndicatorRingMesh();
                indicatorMeshFilter.sharedMesh = this._placementIndicatorRingMesh;
            }

            this.SetPlacementIndicatorVisible(false);
        }

        private void SetPlacementIndicator(Vector3 position, Vector3 normal)
        {
            this.EnsurePlacementIndicator();
            if (this._placementIndicator == null)
            {
                return;
            }

            Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            this._placementIndicator.transform.SetPositionAndRotation(
                position + safeNormal * this.placementIndicatorHeight,
                Quaternion.FromToRotation(Vector3.up, safeNormal)
            );
            this._placementIndicator.transform.localScale = new Vector3(
                this.placementIndicatorRadius,
                1f,
                this.placementIndicatorRadius
            );
        }

        private void SetPlacementIndicatorVisible(bool isVisible)
        {
            if (this._placementIndicator != null)
            {
                this._placementIndicator.SetActive(isVisible);
            }
        }

        private void SetPlacementIndicatorColor(Color color)
        {
            if (this._placementIndicatorMaterial != null)
            {
                SetMaterialColor(this._placementIndicatorMaterial, color);
            }
        }

        private void ShowCardPlacementPreview(CardEffectType effect)
        {
            if (!this.TryResolveCardPlacementPreview(effect, out GameObject prefab, out var offsets))
            {
                this.DestroyPlacementPreview();
                return;
            }

            this.ShowPlacementPreview(prefab, offsets);
        }

        private void ShowNemesisPlacementPreview()
        {
            if (
                GameNetworkManager.Instance == null
                || !GameNetworkManager.Instance.TryGetNemesisPreview(out GameObject prefab)
            )
            {
                this.DestroyPlacementPreview();
                return;
            }

            this.ShowPlacementPreview(prefab, null);
        }

        private void ShowPlacementPreview(GameObject prefab, IReadOnlyList<Vector3> offsets)
        {
            this._placementPreview ??= new DungeonMasterPlacementPreview();
            this._placementPreview.Show(
                prefab,
                offsets,
                this.GetComponent<PlayerVisualAnimator>().AuraMaterial
            );
        }

        private bool TryResolveCardPlacementPreview(
            CardEffectType effect,
            out GameObject prefab,
            out IReadOnlyList<Vector3> offsets
        )
        {
            offsets = null;
            if (
                BattleManager.Instance != null
                && BattleManager.Instance.TryGetCardPreview(effect, out prefab, out offsets)
            )
            {
                return true;
            }

            if (
                GameNetworkManager.Instance != null
                && GameNetworkManager.Instance.TryGetCardPreview(effect, out prefab)
            )
            {
                return true;
            }

            prefab = null;
            return false;
        }

        private void SetPlacementPreview(Vector3 position, Vector3 normal, bool isVisible)
        {
            if (this._placementPreview == null || !this._placementPreview.IsActive)
            {
                return;
            }

            Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            this._placementPreview.SetTransform(
                position + safeNormal * this.placementPreviewLift,
                safeNormal
            );
            this._placementPreview.SetVisible(isVisible);
        }

        private void SetPlacementPreviewVisible(bool isVisible)
        {
            if (this._placementPreview != null && this._placementPreview.IsActive)
            {
                this._placementPreview.SetVisible(isVisible);
            }
        }

        private static Material CreatePlacementIndicatorMaterial(Material fallbackMaterial)
        {
            Shader shader =
                Shader.Find(PlacementIndicatorShaderName)
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");
            var material = shader != null ? new Material(shader) : new Material(fallbackMaterial);
            material.name = "DungeonMasterPlacementIndicatorMaterial";
            material.hideFlags = HideFlags.DontSave;

            SetMaterialColor(material, new Color(0.1f, 1f, 0.25f, 1f));
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            bool setColor = false;
            if (material.HasProperty(BaseColorProperty))
            {
                material.SetColor(BaseColorProperty, color);
                setColor = true;
            }

            if (material.HasProperty(ColorProperty))
            {
                material.SetColor(ColorProperty, color);
                setColor = true;
            }

            if (!setColor)
            {
                material.color = color;
            }
        }

        private void DestroyPlacementIndicator()
        {
            if (this._placementIndicatorMaterial != null)
            {
                Destroy(this._placementIndicatorMaterial);
                this._placementIndicatorMaterial = null;
            }

            if (this._placementIndicatorRingMesh != null)
            {
                Destroy(this._placementIndicatorRingMesh);
                this._placementIndicatorRingMesh = null;
            }

            if (this._placementIndicator != null)
            {
                Destroy(this._placementIndicator);
                this._placementIndicator = null;
            }
        }

        private static Mesh CreatePlacementIndicatorRingMesh()
        {
            const float outerRadius = 1f;
            const float innerRadius = outerRadius * PlacementIndicatorRingInnerRadius;

            var vertices = new Vector3[PlacementIndicatorRingSegments * 2];
            var triangles = new int[PlacementIndicatorRingSegments * 12];
            for (var i = 0; i < PlacementIndicatorRingSegments; i++)
            {
                float angle = Mathf.PI * 2f * i / PlacementIndicatorRingSegments;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                int vertexIndex = i * 2;

                vertices[vertexIndex] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
                vertices[vertexIndex + 1] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);

                int nextVertexIndex = ((i + 1) % PlacementIndicatorRingSegments) * 2;
                int triangleIndex = i * 12;
                triangles[triangleIndex] = vertexIndex;
                triangles[triangleIndex + 1] = nextVertexIndex;
                triangles[triangleIndex + 2] = vertexIndex + 1;
                triangles[triangleIndex + 3] = vertexIndex + 1;
                triangles[triangleIndex + 4] = nextVertexIndex;
                triangles[triangleIndex + 5] = nextVertexIndex + 1;

                triangles[triangleIndex + 6] = vertexIndex + 1;
                triangles[triangleIndex + 7] = nextVertexIndex;
                triangles[triangleIndex + 8] = vertexIndex;
                triangles[triangleIndex + 9] = nextVertexIndex + 1;
                triangles[triangleIndex + 10] = nextVertexIndex;
                triangles[triangleIndex + 11] = vertexIndex + 1;
            }

            var mesh = new Mesh
            {
                name = "DungeonMasterPlacementIndicatorRing",
                hideFlags = HideFlags.DontSave,
                vertices = vertices,
                triangles = triangles,
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void DestroyPlacementPreview()
        {
            if (this._placementPreview == null)
            {
                return;
            }

            this._placementPreview.Destroy();
            this._placementPreview = null;
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        [Server]
        public bool ServerPlayCard(int handSlot, Vector3 groundPosition)
        {
            if (!this.ServerTryGetCardInHand(handSlot, null, out string cardId, out CardData card))
            {
                return false;
            }

            if (this.Mana < card.ManaCost)
            {
                return false;
            }

            if (!this.ServerExecuteCardEffect(card, groundPosition))
            {
                return false;
            }

            this.Mana = Mathf.Max(0f, this.Mana - card.ManaCost);
            this.ServerReplacePlayedCard(handSlot, cardId);
            return true;
        }

        [Server]
        private bool ServerCommitCardCharge(int handSlot, string cardId)
        {
            if (this._committedHandSlot >= 0)
            {
                return false;
            }

            if (!this.ServerTryGetCardInHand(handSlot, cardId, out _, out CardData card))
            {
                return false;
            }

            if (!this.ServerTrySpendMana(card.ManaCost))
            {
                return false;
            }

            this._committedHandSlot = handSlot;
            this._committedCardId = cardId;
            return true;
        }

        [Server]
        private bool ServerPlayCommittedCard(int handSlot, string cardId, Vector3 groundPosition)
        {
            if (this._committedHandSlot != handSlot || this._committedCardId != cardId)
            {
                return false;
            }

            if (!this.ServerTryGetCardInHand(handSlot, cardId, out _, out CardData card))
            {
                this.ServerClearCommittedCardCharge();
                return false;
            }

            if (!this.ServerExecuteCardEffect(card, groundPosition))
            {
                this.Mana = Mathf.Min(this.maxMana, this.Mana + card.ManaCost);
                this.ServerClearCommittedCardCharge();
                return false;
            }

            this.ServerReplacePlayedCard(handSlot, cardId);
            this.ServerClearCommittedCardCharge();
            return true;
        }

        [Server]
        private bool ServerTryGetCardInHand(
            int handSlot,
            string expectedCardId,
            out string cardId,
            out CardData card
        )
        {
            cardId = null;
            card = default;

            if (!this.Player.IsDungeonMaster)
            {
                return false;
            }

            if (handSlot < 0 || handSlot >= this._hand.Count)
            {
                return false;
            }

            cardId = this._hand[handSlot];
            if (string.IsNullOrEmpty(cardId))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(expectedCardId) && cardId != expectedCardId)
            {
                return false;
            }

            var cardData = DCard.GetDataById(cardId);
            if (cardData == null)
            {
                Debug.LogWarning($"[DungeonMasterCardManager] Unknown card id '{cardId}'.");
                return false;
            }

            card = cardData.Value;
            return true;
        }

        [Server]
        private void ServerReplacePlayedCard(int handSlot, string cardId)
        {
            this._used.Add(cardId);
            this._hand[handSlot] = this.ServerDrawReplacementCardId(cardId);
            this.HandCardIds[handSlot] = this._hand[handSlot];
        }

        [Server]
        private void ServerClearCommittedCardCharge()
        {
            this._committedHandSlot = -1;
            this._committedCardId = null;
        }

        [Server]
        private bool ServerExecuteCardEffect(CardData card, Vector3 groundPosition)
        {
            switch (card.Effect)
            {
                case CardEffectType.SPAWN_BASIC_ZOMBIE:
                    var battleManager = BattleManager.Instance;
                    if (battleManager == null)
                    {
                        return false;
                    }

                    return battleManager.ServerTrySpawnBasicZombie(
                        this.Player.localManager,
                        groundPosition
                    );

                case CardEffectType.SPAWN_CREEPER_ZOMBIE:
                    var creeperBattleManager = BattleManager.Instance;
                    if (creeperBattleManager == null)
                    {
                        return false;
                    }

                    return creeperBattleManager.ServerTrySpawnCreeperZombie(
                        this.Player.localManager,
                        groundPosition
                    );

                case CardEffectType.SPAWN_GROUP_OF_DOGS:
                    var dogBattleManager = BattleManager.Instance;
                    if (dogBattleManager == null)
                    {
                        return false;
                    }

                    return dogBattleManager.ServerTrySpawnGroupOfDogs(
                        this.Player.localManager,
                        groundPosition
                    );

                case CardEffectType.SPAWN_MIMIC_ZOMBIE:
                    var mimicBattleManager = BattleManager.Instance;
                    if (mimicBattleManager == null)
                    {
                        return false;
                    }

                    return mimicBattleManager.ServerTrySpawnMimicZombie(
                        this.Player.localManager,
                        groundPosition
                    );

                case CardEffectType.PLACE_BEAR_TRAP:
                    return this.Player.TrapController.ServerPlaceFromCard(
                        TrapType.BearTrap,
                        groundPosition,
                        Vector3.up
                    );

                case CardEffectType.PLACE_C4:
                    return this.Player.TrapController.ServerPlaceFromCard(
                        TrapType.C4,
                        groundPosition,
                        Vector3.up
                    );

                case CardEffectType.PLACE_FLASHBANG:
                    return this.Player.TrapController.ServerPlaceFromCard(
                        TrapType.Flashbang,
                        groundPosition,
                        Vector3.up
                    );

                case CardEffectType.DEPLOY_TURRET:
                    return this.Player.Turret.ServerSpawnTurretForCard(groundPosition);

                case CardEffectType.DEPLOY_SLOWING_TURRET:
                    return this.Player.Turret.ServerSpawnSlowingTurretForCard(groundPosition);

                default:
                    Debug.LogWarning(
                        $"[DungeonMasterCardManager] Unhandled card effect '{card.Effect}'."
                    );
                    return false;
            }
        }

        [Server]
        private void ServerInitializeDeck()
        {
            this._deck.Clear();
            this._used.Clear();
            this._hand.Clear();
            this.HandCardIds.Clear();

            var ids = this.ServerBuildDeckCardIdsFromData();
            ServerShuffle(ids);
            foreach (var id in ids)
            {
                this._deck.Enqueue(id);
            }

            for (var slot = 0; slot < HandSize; slot++)
            {
                var cardId = this.ServerDrawCardId();
                this._hand.Add(cardId);
                this.HandCardIds.Add(cardId);
            }
        }

        [Server]
        private List<string> ServerBuildDeckCardIdsFromData()
        {
            var ids = new List<string>();
            var cardData = DCard.GetAllData();
            if (cardData?.Data == null)
            {
                return ids;
            }

            foreach (var card in cardData.Data)
            {
                if (!string.IsNullOrEmpty(card.CardId))
                {
                    ids.Add(card.CardId);
                }
            }

            return ids;
        }

        [Server]
        private string ServerDrawCardId()
        {
            if (this._debugForceCard && !string.IsNullOrEmpty(this._debugForcedCardId))
            {
                return this._debugForcedCardId;
            }

            if (this._deck.Count == 0)
            {
                this.ServerReshuffleUsedIntoDeck();
            }

            return this._deck.Count > 0 ? this._deck.Dequeue() : null;
        }

        [Server]
        private string ServerDrawReplacementCardId(string replacedCardId)
        {
            var cardId = this.ServerDrawCardId();
            if (cardId != replacedCardId || this._deck.Count == 0)
            {
                return cardId;
            }

            this._deck.Enqueue(cardId);
            return this._deck.Dequeue();
        }

        [Server]
        private void ServerReshuffleUsedIntoDeck()
        {
            if (this._used.Count == 0)
            {
                return;
            }

            ServerShuffle(this._used);
            foreach (var id in this._used)
            {
                this._deck.Enqueue(id);
            }

            this._used.Clear();
        }

        private static void ServerShuffle<T>(List<T> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = UnityEngine.Random.Range(0, i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }
        }

        private void NotifyPlacementStateChanged()
        {
            this.OnPlacementStateChangedEvent?.Invoke();
        }
    }

    public sealed class DungeonMasterPlacementPreview
    {
        private static readonly int AuraColorProperty = Shader.PropertyToID("_AuraColor");
        private static readonly int EdgeColorProperty = Shader.PropertyToID("_EdgeColor");
        private static readonly int AlphaProperty = Shader.PropertyToID("_Alpha");
        private static readonly int FresnelAlphaProperty = Shader.PropertyToID("_FresnelAlpha");

        private static readonly Color PlacementAuraColor = new(0.05f, 1f, 0.22f, 0.82f);
        private static readonly Color PlacementEdgeColor = new(0.92f, 1f, 0.86f, 1f);

        private GameObject _root;
        private Material _previewMaterial;

        public bool IsActive => this._root != null;

        public bool Show(GameObject prefab, IReadOnlyList<Vector3> offsets, Material sourceMaterial)
        {
            this.Destroy();
            if (prefab == null || sourceMaterial == null)
            {
                return false;
            }

            this._previewMaterial = CreatePreviewMaterial(sourceMaterial);
            this._root = new GameObject("DungeonMasterPlacementPreview")
            {
                hideFlags = HideFlags.DontSave,
            };
            this._root.SetActive(false);

            int previewCount = offsets == null || offsets.Count == 0 ? 1 : offsets.Count;
            for (var i = 0; i < previewCount; i++)
            {
                Vector3 offset = offsets == null || offsets.Count == 0 ? Vector3.zero : offsets[i];
                var previewObject = Object.Instantiate(prefab, this._root.transform);
                previewObject.name = $"{prefab.name}_Preview";
                previewObject.transform.localPosition = offset;
                previewObject.transform.localRotation = Quaternion.identity;
                previewObject.transform.localScale = Vector3.one;
                ConfigurePreviewObject(previewObject, this._previewMaterial);
            }

            return true;
        }

        public void SetTransform(Vector3 position, Vector3 normal)
        {
            if (this._root == null)
            {
                return;
            }

            Vector3 safeNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
            this._root.transform.SetPositionAndRotation(
                position,
                Quaternion.FromToRotation(Vector3.up, safeNormal)
            );
        }

        public void SetVisible(bool isVisible)
        {
            if (this._root != null)
            {
                this._root.SetActive(isVisible);
            }
        }

        public void Destroy()
        {
            if (this._root != null)
            {
                this._root.SetActive(false);
                Object.Destroy(this._root);
                this._root = null;
            }

            if (this._previewMaterial != null)
            {
                Object.Destroy(this._previewMaterial);
                this._previewMaterial = null;
            }
        }

        private static Material CreatePreviewMaterial(Material sourceMaterial)
        {
            var material = new Material(sourceMaterial)
            {
                name = "DungeonMasterPlacementPreviewMaterial",
                hideFlags = HideFlags.DontSave,
            };

            if (material.HasProperty(AuraColorProperty))
            {
                material.SetColor(AuraColorProperty, PlacementAuraColor);
            }

            if (material.HasProperty(EdgeColorProperty))
            {
                material.SetColor(EdgeColorProperty, PlacementEdgeColor);
            }

            if (material.HasProperty(AlphaProperty))
            {
                material.SetFloat(AlphaProperty, PlacementAuraColor.a);
            }

            if (material.HasProperty(FresnelAlphaProperty))
            {
                material.SetFloat(FresnelAlphaProperty, 1.35f);
            }

            return material;
        }

        private static void ConfigurePreviewObject(GameObject previewObject, Material previewMaterial)
        {
            foreach (var networkBehaviour in previewObject.GetComponentsInChildren<NetworkBehaviour>(true))
            {
                networkBehaviour.enabled = false;
            }

            foreach (var behaviour in previewObject.GetComponentsInChildren<Behaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (var networkIdentity in previewObject.GetComponentsInChildren<NetworkIdentity>(true))
            {
                Object.Destroy(networkIdentity);
            }

            foreach (var collider in previewObject.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            foreach (var rigidbody in previewObject.GetComponentsInChildren<Rigidbody>(true))
            {
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
                rigidbody.detectCollisions = false;
            }

            foreach (var agent in previewObject.GetComponentsInChildren<NavMeshAgent>(true))
            {
                agent.enabled = false;
            }

            foreach (var audioSource in previewObject.GetComponentsInChildren<AudioSource>(true))
            {
                audioSource.Stop();
                audioSource.enabled = false;
            }

            foreach (var particleSystem in previewObject.GetComponentsInChildren<ParticleSystem>(true))
            {
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.gameObject.SetActive(false);
            }

            foreach (var renderer in previewObject.GetComponentsInChildren<Renderer>(true))
            {
                ApplyPreviewMaterial(renderer, previewMaterial);
                renderer.enabled = true;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
            }
        }

        private static void ApplyPreviewMaterial(Renderer renderer, Material previewMaterial)
        {
            int slotCount = GetPreviewMaterialSlotCount(renderer);
            var materials = new Material[slotCount];
            for (var i = 0; i < materials.Length; i++)
            {
                materials[i] = previewMaterial;
            }

            renderer.sharedMaterials = materials;
        }

        private static int GetPreviewMaterialSlotCount(Renderer renderer)
        {
            int slotCount = renderer.sharedMaterials.Length;
            if (renderer is SkinnedMeshRenderer skinnedRenderer && skinnedRenderer.sharedMesh != null)
            {
                slotCount = Mathf.Max(slotCount, skinnedRenderer.sharedMesh.subMeshCount);
            }
            else if (renderer is MeshRenderer)
            {
                var meshFilter = renderer.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    slotCount = Mathf.Max(slotCount, meshFilter.sharedMesh.subMeshCount);
                }
            }

            return Mathf.Max(1, slotCount);
        }
    }
}
