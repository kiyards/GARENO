using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Display-only Dungeon Master card slot. Renders a single hand card's name and mana cost,
    /// and darkens itself when the card costs more than the current mana. Not interactable yet —
    /// click-to-select arrives with the placement/targeting follow-up.
    /// </summary>
    public class UICardSlot : MonoBehaviour, IPointerClickHandler
    {
        [Header("Scene References")]
        [SerializeField] private TextMeshProUGUI cardNameText;
        [SerializeField] private TextMeshProUGUI manaCostText;

        // Translucent black overlay shown when the card is unaffordable (GDD: darkened card).
        [SerializeField] private GameObject darkenOverlay;

        // Optional card art; left unassigned this slice (text-only cards).
        [SerializeField] private Image cardImage;

        private UIDungeonMasterHand _owner;
        private int _slotIndex = -1;
        private bool _isInteractable;

        private void Awake()
        {
            EnsureRaycastTarget();
        }

        public void Bind(UIDungeonMasterHand owner, int slotIndex)
        {
            this._owner = owner;
            this._slotIndex = slotIndex;
        }

        public void Unbind()
        {
            this._owner = null;
            this._slotIndex = -1;
        }

        /// <summary>
        /// Fill this slot from a hand card id and the Dungeon Master's current mana.
        /// A null/empty id renders an empty, non-highlighted slot.
        /// </summary>
        public void SetCard(string cardId, float currentMana)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                this.SetEmpty();
                return;
            }

            var data = DCard.GetDataById(cardId);
            if (data == null)
            {
                this.SetEmpty();
                return;
            }

            var card = data.Value;
            this._isInteractable = currentMana >= card.ManaCost;

            if (this.cardNameText != null)
            {
                this.cardNameText.text = card.DisplayName;
            }

            if (this.manaCostText != null)
            {
                this.manaCostText.text = card.ManaCost.ToString();
            }

            if (this.cardImage != null)
            {
                this.cardImage.enabled = true;
            }

            this.SetDarkened(!this._isInteractable);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!this._isInteractable ||
                this._owner == null ||
                this._slotIndex < 0 ||
                eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }

            this._owner.SelectSlot(this._slotIndex);
        }

        private void SetEmpty()
        {
            this._isInteractable = false;

            if (this.cardNameText != null)
            {
                this.cardNameText.text = string.Empty;
            }

            if (this.manaCostText != null)
            {
                this.manaCostText.text = string.Empty;
            }

            if (this.cardImage != null)
            {
                this.cardImage.enabled = false;
            }

            this.SetDarkened(true);
        }

        private void SetDarkened(bool darkened)
        {
            if (this.darkenOverlay != null)
            {
                this.darkenOverlay.SetActive(darkened);
            }
        }

        private void EnsureRaycastTarget()
        {
            if (TryGetComponent(out Graphic graphic))
            {
                graphic.raycastTarget = true;
                return;
            }

            var image = gameObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
        }
    }
}
