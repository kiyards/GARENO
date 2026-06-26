using ProjectRuntime.Actor;
using ProjectRuntime.Combat;
using ProjectRuntime.UI;
using ProjectRuntime.Network;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
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
        [field: FormerlySerializedAs("<MastermindOnlyUIParent>k__BackingField")]
        private GameObject DungeonMasterOnlyUIParent { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI RoleMessageTMP { get; set; }

        [field: SerializeField, Header("Player Health")]
        private FlashFillBar PlayerHealthBar { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI PlayerHealthTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI PlayerAmmoTMP { get; set; }

        private Health BoundHealth { get; set; }
        private PistolWeapon BoundWeapon { get; set; }

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
            this.UnbindCombat();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void SetLocalPlayer(PlayerManager player)
        {
            this.LocalPlayer = player;
            this.SetRole(player != null ? player.playerRole : PlayerRole.Unassigned);
            this.BindCombat(player != null ? player.player : null);
        }

        private void BindCombat(GameplayPlayer gameplayPlayer)
        {
            this.UnbindCombat();
            if (gameplayPlayer == null)
            {
                return;
            }

            this.EnsureHudReferences();

            this.BoundHealth = gameplayPlayer.health;
            if (this.BoundHealth != null)
            {
                this.BoundHealth.OnHealthChangedEvent += this.OnHealthChanged;
                this.OnHealthChanged(this.BoundHealth.CurrentHealth, this.BoundHealth.MaxHealth);
            }

            this.BoundWeapon = gameplayPlayer.GetComponent<PistolWeapon>();
            if (this.BoundWeapon != null)
            {
                this.BoundWeapon.OnAmmoChangedEvent += this.OnAmmoChanged;
                this.OnAmmoChanged(this.BoundWeapon.CurrentAmmo, this.BoundWeapon.MagazineSize);
            }
        }

        private void UnbindCombat()
        {
            if (this.BoundHealth != null)
            {
                this.BoundHealth.OnHealthChangedEvent -= this.OnHealthChanged;
                this.BoundHealth = null;
            }

            if (this.BoundWeapon != null)
            {
                this.BoundWeapon.OnAmmoChangedEvent -= this.OnAmmoChanged;
                this.BoundWeapon = null;
            }
        }

        private void OnHealthChanged(float current, float max)
        {
            if (this.PlayerHealthBar != null && max > 0f)
            {
                this.PlayerHealthBar.FillAmount = current / max;
            }

            if (this.PlayerHealthTMP != null)
            {
                this.PlayerHealthTMP.text = Mathf.CeilToInt(Mathf.Max(0f, current)).ToString();
            }
        }

        private void OnAmmoChanged(int current, int magazineSize)
        {
            if (this.PlayerAmmoTMP != null)
            {
                this.PlayerAmmoTMP.text = $"{current}/∞";
            }
        }

        public void SetRole(PlayerRole role)
        {
            this.CurrentRole = role;
            this.EnsureHudReferences();

            if (this.RoleMessageTMP != null)
            {
                this.RoleMessageTMP.text = role == PlayerRole.Unassigned
                    ? "Assigning role..."
                    : $"You are a {GetRoleDisplayName(role)}";
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

            if (this.DungeonMasterOnlyUIParent == null)
            {
                this.DungeonMasterOnlyUIParent = this.CreateChild("DungeonMasterOnlyUI").gameObject;
            }

            if (this.RoleMessageTMP == null)
            {
                this.RoleMessageTMP = this.CreateRoleMessage(this.SharedUIParent.transform);
            }

            if (this.PlayerHealthTMP == null)
            {
                this.PlayerHealthTMP = this.CreateCornerText(
                    this.SurvivorOnlyUIParent.transform, "PlayerHealth",
                    new Vector2(0f, 0f), new Vector2(32f, 32f), TextAlignmentOptions.BottomLeft, "100");
            }

            if (this.PlayerAmmoTMP == null)
            {
                this.PlayerAmmoTMP = this.CreateCornerText(
                    this.SurvivorOnlyUIParent.transform, "PlayerAmmo",
                    new Vector2(1f, 0f), new Vector2(-32f, 32f), TextAlignmentOptions.BottomRight, "6/∞");
            }
        }

        private void ApplyHudVisibility()
        {
            if (this.SharedUIParent == null ||
                this.SurvivorOnlyUIParent == null ||
                this.DungeonMasterOnlyUIParent == null)
            {
                return;
            }

            this.SharedUIParent.SetActive(this.IsPlayerUiVisible);
            this.SurvivorOnlyUIParent.SetActive(this.IsPlayerUiVisible && this.CurrentRole == PlayerRole.Survivor);
            this.DungeonMasterOnlyUIParent.SetActive(this.IsPlayerUiVisible && this.CurrentRole == PlayerRole.DungeonMaster);
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

        private TextMeshProUGUI CreateCornerText(Transform parent, string objectName, Vector2 anchor,
            Vector2 anchoredPosition, TextAlignmentOptions alignment, string initialText)
        {
            var textObject = new GameObject(objectName, typeof(RectTransform));
            var rectTransform = textObject.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = anchor;
            rectTransform.anchorMax = anchor;
            rectTransform.pivot = anchor;
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = new Vector2(360f, 80f);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.alignment = alignment;
            text.color = Color.white;
            text.fontSize = 36f;
            text.raycastTarget = false;
            text.text = initialText;
            return text;
        }

        private static string GetRoleDisplayName(PlayerRole role)
        {
            return role == PlayerRole.DungeonMaster
                ? "Dungeon Master"
                : role.ToString();
        }
    }
}
