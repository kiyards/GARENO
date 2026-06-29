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
    public class DungeonMasterCardManager : NetworkBehaviour
    {
        private const int HandSize = 4;

        // Hardcoded prototype deck — card ids resolved against the DCard data table.
        // Future iterations let the Dungeon Master build their own deck.
        private static readonly string[] StartingDeckCardIds =
        {
            "CARD_BASIC_ZOMBIE",
            "CARD_BASIC_ZOMBIE",
            "CARD_BASIC_ZOMBIE",
            "CARD_BASIC_ZOMBIE",
        };

        [SerializeField] private float manaRegenRate = 1f;
        [SerializeField] private int maxMana = 10;

        [Header("Placement")]
        [SerializeField] private float placementRayDistance = 1000f;
        [SerializeField] private float placementIndicatorRadius = 1.5f;
        [SerializeField] private float placementIndicatorHeight = 0.03f;

        [SyncVar(hook = nameof(OnManaChanged))]
        public float Mana;

        public event Action<float, int> OnManaChangedEvent;
        public int MaxMana => this.maxMana;

        // Replicated mirror of the server-authoritative _hand so the owning Dungeon Master's HUD
        // can render the 4 cards. Server writes it; clients read it. A null/empty entry marks an
        // empty hand slot (the deck and used pile have run dry).
        public readonly SyncList<string> HandCardIds = new();

        // Raised on every peer when the hand contents change. The hand HUD subscribes to refresh.
        public event Action OnHandChangedEvent;

        // Server-only deck state. Stores card ids; null/empty marks an empty hand slot.
        private readonly List<string> _hand = new();
        private readonly Queue<string> _deck = new();
        private readonly List<string> _used = new();

        private GameplayPlayer _player;
        private GameplayPlayer Player => this._player ??= this.GetComponent<GameplayPlayer>();

        private GameObject _placementIndicator;
        private Material _placementIndicatorMaterial;
        private int _selectedHandSlot = -1;
        private bool _isPlacementActive;
        private bool _hasPlacementPoint;
        private Vector3 _placementPosition;
        private Vector3 _placementNormal = Vector3.up;

        private void Awake()
        {
            this.HandCardIds.OnChange += this.OnHandCardsChanged;
        }

        private void OnDestroy()
        {
            this.HandCardIds.OnChange -= this.OnHandCardsChanged;
            this.DestroyPlacementIndicator();
        }

        private void OnHandCardsChanged(SyncList<string>.Operation op, int index, string item)
            => this.OnHandChangedEvent?.Invoke();

        public override void OnStartServer()
        {
            base.OnStartServer();
            this.ServerInitializeDeck();
        }

        private void Update()
        {
            if (this.isServer)
            {
                this.ServerTickMana();
            }

            if (this.isLocalPlayer)
            {
                this.ClientTickPlacement();
            }
        }

        [Server]
        private void ServerTickMana()
        {
            if (!this.Player.IsDungeonMaster) return;
            this.Mana = Mathf.Min(this.Mana + this.manaRegenRate * Time.deltaTime, this.maxMana);
        }

        private void OnManaChanged(float oldVal, float newVal)
            => this.OnManaChangedEvent?.Invoke(newVal, this.maxMana);

        [Server]
        public bool ServerTrySpendMana(int amount)
        {
            if (this.Mana < amount) return false;
            this.Mana -= amount;
            return true;
        }

        [Command]
        public void CmdPlayCard(int handSlot, Vector3 groundPosition)
        {
            this.ServerPlayCard(handSlot, groundPosition);
        }

        private void ClientTickPlacement()
        {
            if (!this.CanUseLocalPlacement())
            {
                this.CancelPlacement();
                return;
            }

            if (!this._isPlacementActive)
            {
                return;
            }

            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                this.CancelPlacement();
                return;
            }

            this.UpdatePlacementPoint(mouse);
            if (this._hasPlacementPoint &&
                mouse.leftButton.wasPressedThisFrame &&
                !IsPointerOverUi())
            {
                this.PlaySelectedCardAtPlacementPoint();
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
            return player != null &&
                   player.IsDungeonMaster &&
                   player.currentState is DungeonMasterMovementState;
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
            if (card.Effect != CardEffectType.SPAWN_BASIC_ZOMBIE || this.Mana < card.ManaCost)
            {
                return;
            }

            this._selectedHandSlot = handSlot;
            this._isPlacementActive = true;
            this._hasPlacementPoint = false;
            this.EnsurePlacementIndicator();
            this.SetPlacementIndicatorVisible(false);
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
            this.SetPlacementIndicatorVisible(true);
        }

        private void PlaySelectedCardAtPlacementPoint()
        {
            int handSlot = this._selectedHandSlot;
            Vector3 groundPosition = this._placementPosition;
            this.CancelPlacement();
            this.CmdPlayCard(handSlot, groundPosition);
        }

        private void CancelPlacement()
        {
            this._selectedHandSlot = -1;
            this._isPlacementActive = false;
            this._hasPlacementPoint = false;
            this.SetPlacementIndicatorVisible(false);
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
            if (!Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    this.placementRayDistance,
                    1 << groundLayer,
                    QueryTriggerInteraction.Ignore))
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
            this._placementIndicator.name = "DungeonMasterBasicZombiePlacementIndicator";

            if (this._placementIndicator.TryGetComponent(out Collider indicatorCollider))
            {
                Destroy(indicatorCollider);
            }

            if (this._placementIndicator.TryGetComponent(out Renderer indicatorRenderer))
            {
                this._placementIndicatorMaterial = new Material(indicatorRenderer.sharedMaterial)
                {
                    color = new Color(0.1f, 1f, 0.25f, 0.65f)
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
                Quaternion.FromToRotation(Vector3.up, safeNormal));
            this._placementIndicator.transform.localScale = new Vector3(
                this.placementIndicatorRadius * 2f,
                this.placementIndicatorHeight,
                this.placementIndicatorRadius * 2f);
        }

        private void SetPlacementIndicatorVisible(bool isVisible)
        {
            if (this._placementIndicator != null)
            {
                this._placementIndicator.SetActive(isVisible);
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
            if (!this.Player.IsDungeonMaster)
            {
                return false;
            }

            if (handSlot < 0 || handSlot >= this._hand.Count)
            {
                return false;
            }

            var cardId = this._hand[handSlot];
            if (string.IsNullOrEmpty(cardId))
            {
                return false;
            }

            var cardData = DCard.GetDataById(cardId);
            if (cardData == null)
            {
                Debug.LogWarning($"[DungeonMasterCardManager] Unknown card id '{cardId}'.");
                return false;
            }

            var card = cardData.Value;
            if (!this.ServerTrySpendMana(card.ManaCost))
            {
                return false;
            }

            if (!this.ServerExecuteCardEffect(card, groundPosition))
            {
                // Effect failed (e.g. invalid spawn position) — refund the mana and keep the card.
                this.Mana = Mathf.Min(this.Mana + card.ManaCost, this.maxMana);
                return false;
            }

            this._used.Add(cardId);
            this._hand[handSlot] = this.ServerDrawCardId();
            this.HandCardIds[handSlot] = this._hand[handSlot];
            return true;
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
                        groundPosition);
                default:
                    Debug.LogWarning(
                        $"[DungeonMasterCardManager] Unhandled card effect '{card.Effect}'.");
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

            var ids = new List<string>(StartingDeckCardIds);
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
        private string ServerDrawCardId()
        {
            if (this._deck.Count == 0)
            {
                this.ServerReshuffleUsedIntoDeck();
            }

            return this._deck.Count > 0 ? this._deck.Dequeue() : null;
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
    }
}
