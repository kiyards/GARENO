using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Renders a Dungeon Master hand card and forwards hover/click input to the hand controller.
    /// </summary>
    public class UICardSlot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
    {
        [Header("Scene References")]
        [SerializeField] private TextMeshProUGUI cardNameText;
        [SerializeField] private TextMeshProUGUI manaCostText;
        [SerializeField] private float hoverLift = 24f;
        [SerializeField] private float hoverMoveDuration = 0.1f;

        // Translucent black overlay shown when the card is unaffordable (GDD: darkened card).
        [SerializeField] private GameObject darkenOverlay;

        // Optional card art; left unassigned this slice (text-only cards).
        [SerializeField] private Image cardImage;

        private UIDungeonMasterHand _owner;
        private int _slotIndex = -1;
        private string _cardId;
        private float _currentMana;
        private bool _isInteractable;
        private bool _isInteractionLocked;
        private readonly List<RectTransform> _hoverTargets = new();
        private readonly List<Vector3> _hoverTargetBaseLocalPositions = new();
        private bool _isHovered;
        private bool _isHoverAnimating;

        private void Awake()
        {
            this.CacheHoverTargets();
            EnsureRaycastTarget();
        }

        private void OnDisable()
        {
            this.SetHovered(false, instant: true);
        }

        private void Update()
        {
            if (!this._isHoverAnimating)
            {
                return;
            }

            this.TickHoverAnimation();
        }

        public void Bind(UIDungeonMasterHand owner, int slotIndex)
        {
            this._owner = owner;
            this._slotIndex = slotIndex;
        }

        public void Unbind()
        {
            this.SetHovered(false, instant: true);
            this._owner = null;
            this._slotIndex = -1;
        }

        /// <summary>
        /// Fill this slot from a hand card id and the Dungeon Master's current mana.
        /// A null/empty id renders an empty, non-highlighted slot.
        /// </summary>
        public void SetCard(string cardId, float currentMana)
        {
            this._cardId = cardId;
            this._currentMana = currentMana;

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
            this._isInteractable = currentMana >= card.ManaCost && !this._isInteractionLocked;

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

        public void SetInteractionLocked(bool isLocked)
        {
            if (this._isInteractionLocked == isLocked)
            {
                return;
            }

            this._isInteractionLocked = isLocked;
            this.SetCard(this._cardId, this._currentMana);
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

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (string.IsNullOrEmpty(this._cardId))
            {
                return;
            }

            this.SetHovered(true);
            this._owner?.HoverSlot(this._slotIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            this.SetHovered(false);
            this._owner?.UnhoverSlot(this._slotIndex);
        }

        private void SetEmpty()
        {
            this._cardId = null;
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

        private void SetHovered(bool isHovered, bool instant = false)
        {
            this.CacheHoverTargets();
            if (this._isHovered == isHovered && this._hoverTargets.Count > 0)
            {
                return;
            }

            this._isHovered = isHovered;
            this._isHoverAnimating = true;

            if (instant || this.hoverMoveDuration <= 0f)
            {
                this.ApplyHoverOffset(isHovered ? Vector3.up * this.hoverLift : Vector3.zero);
                this._isHoverAnimating = false;
            }
        }

        private void TickHoverAnimation()
        {
            this.CacheHoverTargets();
            if (this._hoverTargets.Count == 0)
            {
                this._isHoverAnimating = false;
                return;
            }

            Vector3 targetOffset = this._isHovered ? Vector3.up * this.hoverLift : Vector3.zero;
            float speed = this.hoverMoveDuration > 0f
                ? this.hoverLift / this.hoverMoveDuration
                : float.PositiveInfinity;
            bool allArrived = true;

            for (var i = 0; i < this._hoverTargets.Count; i++)
            {
                if (this._hoverTargets[i] == null)
                {
                    continue;
                }

                Vector3 targetPosition = this._hoverTargetBaseLocalPositions[i] + targetOffset;
                this._hoverTargets[i].localPosition = Vector3.MoveTowards(
                    this._hoverTargets[i].localPosition,
                    targetPosition,
                    speed * Time.unscaledDeltaTime);

                if ((this._hoverTargets[i].localPosition - targetPosition).sqrMagnitude > 0.01f)
                {
                    allArrived = false;
                }
            }

            if (allArrived)
            {
                this.ApplyHoverOffset(targetOffset);
                this._isHoverAnimating = false;
            }
        }

        private void ApplyHoverOffset(Vector3 offset)
        {
            for (var i = 0; i < this._hoverTargets.Count; i++)
            {
                if (this._hoverTargets[i] != null)
                {
                    this._hoverTargets[i].localPosition = this._hoverTargetBaseLocalPositions[i] + offset;
                }
            }
        }

        private void CacheHoverTargets()
        {
            if (this._hoverTargets.Count > 0)
            {
                return;
            }

            foreach (Transform child in this.transform)
            {
                if (child is RectTransform childRect)
                {
                    this._hoverTargets.Add(childRect);
                    this._hoverTargetBaseLocalPositions.Add(childRect.localPosition);
                }
            }

            if (this._hoverTargets.Count == 0 && this.transform is RectTransform selfRect)
            {
                this._hoverTargets.Add(selfRect);
                this._hoverTargetBaseLocalPositions.Add(selfRect.localPosition);
            }
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
