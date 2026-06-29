using System;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Managers;
using UnityEngine;

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

        private void Awake()
        {
            this.HandCardIds.OnChange += this.OnHandCardsChanged;
        }

        private void OnDestroy()
        {
            this.HandCardIds.OnChange -= this.OnHandCardsChanged;
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
            if (!this.isServer) return;
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
