using TMPro;
using UnityEngine;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Non-networked floating damage number. Projects a world hit point into the HUD canvas,
    /// floats upward in UI space and fades out, then self-destructs.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class DamagePopup : MonoBehaviour
    {
        private const string RootName = "DamagePopupRoot";

        [SerializeField] private float lifetime = 0.7f;
        [SerializeField] private float riseSpeed = 90f;

        private RectTransform _rectTransform;
        private TextMeshProUGUI _text;
        private float _elapsed;
        private Color _baseColor;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            _text = GetComponent<TextMeshProUGUI>();
            _baseColor = _text.color;
        }

        /// <summary>Spawn a popup at a world position showing the given amount.</summary>
        public static void Spawn(DamagePopup prefab, Vector3 worldPos, float amount)
        {
            if (prefab == null) return;

            Camera worldCamera = Camera.main;
            if (worldCamera == null) return;

            Vector3 screenPoint = worldCamera.WorldToScreenPoint(worldPos);
            if (screenPoint.z <= 0f) return;

            RectTransform root = FindDamagePopupRoot();
            if (root == null) return;

            Canvas canvas = root.GetComponentInParent<Canvas>();
            Camera uiCamera =
                canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;
            if (
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    root,
                    screenPoint,
                    uiCamera,
                    out Vector2 anchoredPosition
                )
            )
            {
                return;
            }

            root.SetAsLastSibling();
            var popup = Instantiate(prefab, root);
            popup.Initialize(anchoredPosition, amount);
        }

        public void SetAmount(float amount)
        {
            _text.text = Mathf.RoundToInt(amount).ToString();
        }

        private static RectTransform FindDamagePopupRoot()
        {
            GameObject existing = GameObject.Find(RootName);
            if (existing != null && existing.TryGetComponent(out RectTransform root))
            {
                return root;
            }

            Canvas[] canvases = Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );
            Canvas bestCanvas = null;
            foreach (Canvas canvas in canvases)
            {
                if (!canvas.isRootCanvas)
                    continue;

                if (bestCanvas == null || canvas.sortingOrder > bestCanvas.sortingOrder)
                    bestCanvas = canvas;
            }

            if (bestCanvas == null)
                return null;

            var rootObject = new GameObject(RootName, typeof(RectTransform));
            var rootTransform = rootObject.GetComponent<RectTransform>();
            rootTransform.SetParent(bestCanvas.transform, false);
            rootTransform.anchorMin = Vector2.zero;
            rootTransform.anchorMax = Vector2.one;
            rootTransform.offsetMin = Vector2.zero;
            rootTransform.offsetMax = Vector2.zero;
            rootTransform.SetAsLastSibling();
            return rootTransform;
        }

        private void Initialize(Vector2 anchoredPosition, float amount)
        {
            _rectTransform.anchoredPosition = anchoredPosition;
            SetAmount(amount);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            _rectTransform.anchoredPosition += Vector2.up * (riseSpeed * Time.deltaTime);

            float t = Mathf.Clamp01(_elapsed / lifetime);
            var c = _baseColor;
            c.a = Mathf.Lerp(1f, 0f, t);
            _text.color = c;

            if (_elapsed >= lifetime)
                Destroy(gameObject);
        }
    }
}
