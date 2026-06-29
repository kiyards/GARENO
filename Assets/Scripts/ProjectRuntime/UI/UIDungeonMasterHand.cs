using ProjectRuntime.Actor;
using UnityEngine;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Renders the Dungeon Master's 4-card hand. Binds to the local player's
    /// <see cref="DungeonMasterCardManager"/> (via <see cref="PlayerHudManager"/>) and refreshes
    /// the slots whenever the replicated hand or the mana changes. Display-only this slice;
    /// cards are played through the existing input path.
    /// </summary>
    public class UIDungeonMasterHand : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private UICardSlot[] slots;

        private DungeonMasterCardManager _cardManager;

        /// <summary>Bind to the local Dungeon Master's card manager and start tracking its hand.</summary>
        public void Bind(DungeonMasterCardManager cardManager)
        {
            this.Unbind();

            this._cardManager = cardManager;
            if (this._cardManager == null)
            {
                return;
            }

            this._cardManager.OnHandChangedEvent += this.OnHandChanged;
            this._cardManager.OnManaChangedEvent += this.OnManaChanged;
            this.RefreshHand();
        }

        public void Unbind()
        {
            if (this._cardManager == null)
            {
                return;
            }

            this._cardManager.OnHandChangedEvent -= this.OnHandChanged;
            this._cardManager.OnManaChangedEvent -= this.OnManaChanged;
            this._cardManager = null;
        }

        private void OnDestroy()
        {
            this.Unbind();
        }

        private void OnHandChanged() => this.RefreshHand();

        private void OnManaChanged(float current, int max) => this.RefreshHand();

        private void RefreshHand()
        {
            if (this.slots == null || this._cardManager == null)
            {
                return;
            }

            var hand = this._cardManager.HandCardIds;
            var mana = this._cardManager.Mana;

            for (var i = 0; i < this.slots.Length; i++)
            {
                if (this.slots[i] == null)
                {
                    continue;
                }

                var cardId = i < hand.Count ? hand[i] : null;
                this.slots[i].SetCard(cardId, mana);
            }
        }
    }
}
