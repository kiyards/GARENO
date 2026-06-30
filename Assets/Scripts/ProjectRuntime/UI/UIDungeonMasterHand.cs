using ProjectRuntime.Actor;
using TMPro;
using UnityEngine;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Renders the Dungeon Master's 4-card hand. Binds to the local player's
    /// <see cref="DungeonMasterCardManager"/> (via <see cref="PlayerHudManager"/>) and refreshes
    /// the slots whenever the replicated hand or the mana changes. Clicking a slot asks the
    /// local card manager to enter placement mode for that hand index.
    /// </summary>
    public class UIDungeonMasterHand : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private UICardSlot[] slots;
        [SerializeField] private GameObject handRoot;
        [SerializeField] private TextMeshProUGUI selectedCardNameText;
        [SerializeField] private GameObject cardDescriptionParent;

        private DungeonMasterCardManager _cardManager;
        private int _hoveredSlotIndex = -1;
        private TextMeshProUGUI _hoverCardNameTMP;
        private TextMeshProUGUI _hoverCardDescriptionTMP;
        private string _hoverCardName;
        private string _hoverCardDescription;

        private void Awake()
        {
            this.CacheCardInfoPanelText();
            this.ApplyCardInfoPanel();
        }

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
            this._cardManager.OnPlacementStateChangedEvent += this.OnPlacementStateChanged;
            this.BindSlots();
            this.RefreshHand();
            this.RefreshPlacementUi();
        }

        public void Unbind()
        {
            if (this._cardManager == null)
            {
                return;
            }

            this._cardManager.OnHandChangedEvent -= this.OnHandChanged;
            this._cardManager.OnManaChangedEvent -= this.OnManaChanged;
            this._cardManager.OnPlacementStateChangedEvent -= this.OnPlacementStateChanged;
            this.UnbindSlots();
            this._cardManager = null;
            this._hoveredSlotIndex = -1;
            this.SetSelectedCardName(null);
            this.SetDescription(null);
        }

        private void OnDestroy()
        {
            this.Unbind();
        }

        private void Update()
        {
            if (this._cardManager != null && this._cardManager.IsPlacementCharging)
            {
                this.RefreshPlacementUi();
            }
        }

        private void OnHandChanged()
        {
            this._hoveredSlotIndex = -1;
            this.RefreshHand();
            this.SetHoverCardInfo(null, null);
        }

        private void OnManaChanged(float current, int max) => this.RefreshHand();

        private void OnPlacementStateChanged()
        {
            this.RefreshHand();
            this.RefreshPlacementUi();
        }

        private void RefreshHand()
        {
            if (this.slots == null || this._cardManager == null)
            {
                return;
            }

            var hand = this._cardManager.HandCardIds;
            var mana = this._cardManager.Mana;
            bool handVisible = !this._cardManager.IsPlacementModeActive;
            this.SetHandVisible(handVisible);

            for (var i = 0; i < this.slots.Length; i++)
            {
                if (this.slots[i] == null)
                {
                    continue;
                }

                var cardId = i < hand.Count ? hand[i] : null;
                this.slots[i].SetCard(cardId, mana);
                this.slots[i].SetInteractionLocked(!handVisible);
            }
        }

        private void RefreshPlacementUi()
        {
            if (this._cardManager == null)
            {
                this.SetSelectedCardName(null);
                this.SetHoverCardInfo(null, null);
                return;
            }

            if (!this._cardManager.IsPlacementModeActive)
            {
                this.SetSelectedCardName(null);
                this.RefreshHoverDescription();
                return;
            }

            var data = DCard.GetDataById(this._cardManager.SelectedCardId);
            if (data == null)
            {
                this.SetSelectedCardName(null);
                this.SetHoverCardInfo(null, null);
                return;
            }

            var card = data.Value;
            string selectedText = this._cardManager.IsPlacementCharging
                ? $"{card.DisplayName} {Mathf.RoundToInt(this._cardManager.PlacementChargeProgress * 100f)}%"
                : card.DisplayName;
            this.SetSelectedCardName(selectedText);
            this.SetHoverCardInfo(null, null);
        }

        private void BindSlots()
        {
            if (this.slots == null)
            {
                return;
            }

            for (var i = 0; i < this.slots.Length; i++)
            {
                this.slots[i]?.Bind(this, i);
            }
        }

        private void UnbindSlots()
        {
            if (this.slots == null)
            {
                return;
            }

            foreach (var slot in this.slots)
            {
                slot?.Unbind();
            }
        }

        public void SelectSlot(int slotIndex)
        {
            this._cardManager?.TryBeginPlacementFromHand(slotIndex);
        }

        public void HoverSlot(int slotIndex)
        {
            if (this._cardManager != null && this._cardManager.IsPlacementModeActive)
            {
                return;
            }

            this._hoveredSlotIndex = slotIndex;
            this.RefreshHoverDescription();
        }

        public void UnhoverSlot(int slotIndex)
        {
            if (this._hoveredSlotIndex != slotIndex)
            {
                return;
            }

            this._hoveredSlotIndex = -1;
            this.RefreshHoverDescription();
        }

        private void RefreshHoverDescription()
        {
            if (this._cardManager == null ||
                this._hoveredSlotIndex < 0 ||
                this._hoveredSlotIndex >= this._cardManager.HandCardIds.Count ||
                this._cardManager.IsPlacementModeActive)
            {
                this.SetHoverCardInfo(null, null);
                return;
            }

            var cardId = this._cardManager.HandCardIds[this._hoveredSlotIndex];
            var data = DCard.GetDataById(cardId);
            if (!data.HasValue)
            {
                this.SetHoverCardInfo(null, null);
                return;
            }

            var card = data.Value;
            this.SetHoverCardInfo(card.DisplayName, card.CardDescription);
        }

        private void SetHandVisible(bool isVisible)
        {
            if (this.handRoot != null && this.handRoot != this.gameObject)
            {
                this.handRoot.SetActive(isVisible);
                return;
            }

            if (this.slots == null)
            {
                return;
            }

            foreach (var slot in this.slots)
            {
                if (slot != null)
                {
                    slot.gameObject.SetActive(isVisible);
                }
            }
        }

        private void SetSelectedCardName(string text)
        {
            if (this.selectedCardNameText == null)
            {
                return;
            }

            this.selectedCardNameText.text = text ?? string.Empty;
            this.selectedCardNameText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        private void SetDescription(string text)
        {
            this.SetHoverCardInfo(null, text);
        }

        private void SetHoverCardInfo(string cardName, string cardDescription)
        {
            this._hoverCardName = cardName;
            this._hoverCardDescription = cardDescription;
            this.ApplyCardInfoPanel();
        }

        private void ApplyCardInfoPanel()
        {
            this.CacheCardInfoPanelText();

            bool hasName = !string.IsNullOrEmpty(this._hoverCardName);
            bool hasDescription = !string.IsNullOrEmpty(this._hoverCardDescription);

            if (this._hoverCardNameTMP != null)
            {
                this._hoverCardNameTMP.text = this._hoverCardName ?? string.Empty;
                this._hoverCardNameTMP.gameObject.SetActive(hasName);
            }

            if (this._hoverCardDescriptionTMP != null)
            {
                this._hoverCardDescriptionTMP.text = this._hoverCardDescription ?? string.Empty;
                this._hoverCardDescriptionTMP.gameObject.SetActive(hasDescription);
            }

            if (this.cardDescriptionParent != null)
            {
                this.cardDescriptionParent.SetActive(hasName || hasDescription);
            }
        }

        private void CacheCardInfoPanelText()
        {
            if (this.cardDescriptionParent == null ||
                this._hoverCardNameTMP != null && this._hoverCardDescriptionTMP != null)
            {
                return;
            }

            var textComponents = this.cardDescriptionParent.GetComponentsInChildren<TextMeshProUGUI>(true);
            foreach (var textComponent in textComponents)
            {
                textComponent.raycastTarget = false;

                if (this._hoverCardNameTMP == null && textComponent.name.Contains("Name"))
                {
                    this._hoverCardNameTMP = textComponent;
                    continue;
                }

                if (this._hoverCardDescriptionTMP == null && textComponent.name.Contains("Description"))
                {
                    this._hoverCardDescriptionTMP = textComponent;
                }
            }

            if (this._hoverCardNameTMP == null && textComponents.Length > 0)
            {
                this._hoverCardNameTMP = textComponents[0];
            }

            if (this._hoverCardDescriptionTMP == null && textComponents.Length > 1)
            {
                this._hoverCardDescriptionTMP = textComponents[1];
            }
        }
    }
}
