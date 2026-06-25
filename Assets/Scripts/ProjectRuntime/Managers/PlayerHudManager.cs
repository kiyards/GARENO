using ProjectRuntime.UI;
using ProjectRuntime.Network;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.Managers
{
    public class PlayerHudManager : MonoBehaviour
    {
        public static PlayerHudManager Instance { get; private set; }

        [field: SerializeField, Header("Scene References")]
        private GameObject SharedUIParent { get; set; }

        [field: SerializeField]
        private GameObject SurvivorOnlyUIParent { get; set; }

        [field: SerializeField]
        private GameObject MastermindOnlyUIParent { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI RoleMessageTMP { get; set; }

        [field: SerializeField, Header("Player Health")]
        private FlashFillBar PlayerHealthBar { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI PlayerHealthTMP { get; set; }

        private PlayerManager LocalPlayer { get; set; }
        private PlayerRole CurrentRole { get; set; } = PlayerRole.Unassigned;
        private bool IsPlayerUiVisible { get; set; } = true;

        public static PlayerHudManager EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var existing = FindFirstObjectByType<PlayerHudManager>();
            if (existing != null)
            {
                return existing;
            }

            var hudObject = new GameObject(nameof(PlayerHudManager));
            var canvas = hudObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var canvasScaler = hudObject.AddComponent<CanvasScaler>();
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.matchWidthOrHeight = 0.5f;

            hudObject.AddComponent<GraphicRaycaster>();
            return hudObject.AddComponent<PlayerHudManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
            this.EnsureCanvas();
            this.EnsureHudReferences();
            this.SetRole(PlayerRole.Unassigned);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetLocalPlayer(PlayerManager player)
        {
            this.LocalPlayer = player;
            this.SetRole(player != null ? player.playerRole : PlayerRole.Unassigned);
        }

        public void SetRole(PlayerRole role)
        {
            this.CurrentRole = role;
            this.EnsureHudReferences();

            if (this.RoleMessageTMP != null)
            {
                this.RoleMessageTMP.text = role == PlayerRole.Unassigned
                    ? "Assigning role..."
                    : $"You are a {role}";
            }

            this.ApplyHudVisibility();
        }

        public void TogglePlayerUI(bool toggle)
        {
            this.IsPlayerUiVisible = toggle;
            this.EnsureHudReferences();
            this.ApplyHudVisibility();
        }

        private void EnsureHudReferences()
        {
            if (this.SharedUIParent == null)
            {
                this.SharedUIParent = this.CreateChild("SharedUI").gameObject;
            }

            if (this.SurvivorOnlyUIParent == null)
            {
                this.SurvivorOnlyUIParent = this.CreateChild("SurvivorOnlyUI").gameObject;
            }

            if (this.MastermindOnlyUIParent == null)
            {
                this.MastermindOnlyUIParent = this.CreateChild("MastermindOnlyUI").gameObject;
            }

            if (this.RoleMessageTMP == null)
            {
                this.RoleMessageTMP = this.CreateRoleMessage(this.SharedUIParent.transform);
            }
        }

        private void ApplyHudVisibility()
        {
            if (this.SharedUIParent == null ||
                this.SurvivorOnlyUIParent == null ||
                this.MastermindOnlyUIParent == null)
            {
                return;
            }

            this.SharedUIParent.SetActive(this.IsPlayerUiVisible);
            this.SurvivorOnlyUIParent.SetActive(this.IsPlayerUiVisible && this.CurrentRole == PlayerRole.Survivor);
            this.MastermindOnlyUIParent.SetActive(this.IsPlayerUiVisible && this.CurrentRole == PlayerRole.Mastermind);
        }

        private void EnsureCanvas()
        {
            if (this.GetComponentInParent<Canvas>() != null)
            {
                return;
            }

            var canvas = this.gameObject.GetComponent<Canvas>();
            if (canvas == null)
            {
                canvas = this.gameObject.AddComponent<Canvas>();
            }
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var canvasScaler = this.gameObject.GetComponent<CanvasScaler>();
            if (canvasScaler == null)
            {
                canvasScaler = this.gameObject.AddComponent<CanvasScaler>();
            }
            canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasScaler.referenceResolution = new Vector2(1920, 1080);
            canvasScaler.matchWidthOrHeight = 0.5f;

            if (this.gameObject.GetComponent<GraphicRaycaster>() == null)
            {
                this.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private RectTransform CreateChild(string childName, Transform parent = null)
        {
            var child = new GameObject(childName, typeof(RectTransform));
            var rectTransform = child.GetComponent<RectTransform>();
            rectTransform.SetParent(parent != null ? parent : this.transform, false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            return rectTransform;
        }

        private TextMeshProUGUI CreateRoleMessage(Transform parent)
        {
            var roleMessageObject = new GameObject("RoleMessage", typeof(RectTransform));
            var rectTransform = roleMessageObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = new Vector2(0.5f, 1f);
            rectTransform.anchorMax = new Vector2(0.5f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, -48f);
            rectTransform.sizeDelta = new Vector2(720f, 80f);

            var roleMessage = roleMessageObject.AddComponent<TextMeshProUGUI>();
            roleMessage.alignment = TextAlignmentOptions.Center;
            roleMessage.color = Color.white;
            roleMessage.fontSize = 36f;
            roleMessage.raycastTarget = false;
            roleMessage.text = "Assigning role...";
            return roleMessage;
        }
    }
}
