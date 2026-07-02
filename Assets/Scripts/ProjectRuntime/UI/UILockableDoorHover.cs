using ProjectRuntime.Actor;
using ProjectRuntime.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class UILockableDoorHover : MonoBehaviour
    {
        private static UILockableDoorHover s_instance;

        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TextMeshProUGUI statusText;

        private LockableDoor _door;

        public static UILockableDoorHover Ensure()
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            var existing = FindFirstObjectByType<UILockableDoorHover>(FindObjectsInactive.Include);
            if (existing != null)
            {
                s_instance = existing;
                return s_instance;
            }

            var hud = PlayerHudManager.EnsureInstance();
            Transform parent = hud != null ? hud.transform : null;

            var root = new GameObject(
                "LockableDoorHover",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(UILockableDoorHover));
            root.transform.SetParent(parent, false);

            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, -78f);
            rect.sizeDelta = new Vector2(220f, 44f);

            var background = root.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.72f);
            background.raycastTarget = false;

            var textObject = new GameObject("StatusTMP", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);

            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);

            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.fontSize = 18f;
            text.raycastTarget = false;

            s_instance = root.GetComponent<UILockableDoorHover>();
            s_instance.canvasGroup = root.GetComponent<CanvasGroup>();
            s_instance.statusText = text;
            s_instance.Hide();
            return s_instance;
        }

        private void Awake()
        {
            s_instance = this;
            Hide();
        }

        private void OnDestroy()
        {
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        private void Update()
        {
            if (_door != null)
            {
                statusText.text = _door.GetHoverText();
            }
        }

        public void SetDoor(LockableDoor door)
        {
            _door = door;
            if (_door == null)
            {
                Hide();
                return;
            }

            statusText.text = _door.GetHoverText();
            canvasGroup.alpha = 1f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }

        private void Hide()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }
    }
}
