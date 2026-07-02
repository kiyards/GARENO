using System;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Managers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

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
        private float placementChargeDuration = 1f;

        [SyncVar(hook = nameof(OnManaChanged))]
        public float Mana;

        public event Action<float, int> OnManaChangedEvent;
        public int MaxMana => this.maxMana;

        [Header("Nemesis")]
        [SerializeField] private float nemesisBaseCountdown = 120f;

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

        // Replicated mirror of the server-authoritative _hand so the owning Dungeon Master's HUD
        // can render the 4 cards. Server writes it; clients read it. A null/empty entry marks an
        // empty hand slot (the deck and used pile have run dry).
        public readonly SyncList<string> HandCardIds = new();

        // Raised on every peer when the hand contents change. The hand HUD subscribes to refresh.
        public event Action OnHandChangedEvent;
        public event Action OnPlacementStateChangedEvent;

        // Server-only deck state. Stores card ids; null/empty marks an empty hand slot.
        private readonly List<string> _hand = new();
        private readonly Queue<string> _deck = new();
        private readonly List<string> _used = new();

        private GameplayPlayer _player;
        private GameplayPlayer Player => this._player ??= this.GetComponent<GameplayPlayer>();

        private GameObject _placementIndicator;
        private Material _placementIndicatorMaterial;
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

        public DungeonMasterCardPlacementState PlacementState => this._placementState;
        public bool IsPlacementModeActive =>
            this._placementState != DungeonMasterCardPlacementState.Idle;
        public bool IsPlacementCharging =>
            this._placementState == DungeonMasterCardPlacementState.ChargingPlacement;
        public string SelectedCardId => this._selectedCardId;
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

        // Flips the Nemesis to "used" so the side-card locks out (placeholder until proper Nemesis enemy is implemented)
        [Server]
        public bool ServerTryActivateNemesis()
        {
            if (!this.Player.IsDungeonMaster) return false;
            if (!this._nemesisAvailable || this._nemesisActive) return false;

            this._nemesisActive = true;
            this._nemesisAvailable = false;
            Debug.Log(
                "[DungeonMasterCardManager] Nemesis activated (Task 1 stub — entity spawn lands in Task 2).");
            return true;
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
            this.SetPlacementIndicatorVisible(false);
            this.NotifyPlacementStateChanged();
        }

        private void UpdatePlacementPoint(Mouse mouse)
        {
            if (!this.TryGetGroundPoint(mouse, out Vector3 position, out Vector3 normal))
            {
                this._hasPlacementPoint = false;
                this.SetPlacementIndicatorVisible(false);
                return;
            }

            this._hasPlacementPoint = true;
            this._placementPosition = position;
            this._placementNormal = normal;
            this.SetPlacementIndicator(position, normal);
            this.SetPlacementIndicatorColor(new Color(0.1f, 1f, 0.25f, 0.65f));
            this.SetPlacementIndicatorVisible(true);
        }

        private void BeginPlacementCharge()
        {
            if (!this._hasPlacementPoint)
            {
                return;
            }

            this.CmdCommitCardCharge(this._selectedHandSlot, this._selectedCardId);
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
                this.placementIndicatorRadius * 2f * pulse,
                this.placementIndicatorHeight,
                this.placementIndicatorRadius * 2f * pulse
            );
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
            )
            {
                return;
            }

            this._selectedHandSlot = -1;
            this._selectedCardId = null;
            this._placementState = DungeonMasterCardPlacementState.Idle;
            this._placementStartedFrame = -1;
            this._hasPlacementPoint = false;
            this.SetPlacementIndicatorVisible(false);
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
                this._placementIndicatorMaterial = new Material(indicatorRenderer.sharedMaterial)
                {
                    color = new Color(0.1f, 1f, 0.25f, 0.65f),
                };
                indicatorRenderer.material = this._placementIndicatorMaterial;
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
                this.placementIndicatorRadius * 2f,
                this.placementIndicatorHeight,
                this.placementIndicatorRadius * 2f
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
                this._placementIndicatorMaterial.color = color;
            }
        }

        private void DestroyPlacementIndicator()
        {
            if (this._placementIndicatorMaterial != null)
            {
                Destroy(this._placementIndicatorMaterial);
                this._placementIndicatorMaterial = null;
            }

            if (this._placementIndicator != null)
            {
                Destroy(this._placementIndicator);
                this._placementIndicator = null;
            }
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
}
