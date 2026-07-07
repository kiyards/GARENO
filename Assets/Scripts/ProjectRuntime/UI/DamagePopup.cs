using TMPro;
using UnityEngine;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Non-networked floating damage number. Spawns in world space, renders over
    /// non-wall geometry, hides behind Wall line of sight, then self-destructs.
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public class DamagePopup : MonoBehaviour
    {
        private const string OverlayShaderName = "TextMeshPro/Distance Field Overlay";

        [SerializeField] private float lifetime = 0.7f;
        [SerializeField] private float riseSpeed = 1.2f;
        [SerializeField] private float spawnHeight = 0.75f;
        [SerializeField] private float scatterRadius = 0.25f;
        [SerializeField] private float wallFadeSpeed = 16f;

        private static int _wallMask = -1;

        private TextMeshPro _text;
        private Material _runtimeMaterial;
        private float _elapsed;
        private Color _baseColor;
        private float _visibility = 1f;

        private void Awake()
        {
            _text = GetComponent<TextMeshPro>();
            _baseColor = _text.color;
            ApplyOverlayMaterial();
        }

        /// <summary>Spawn a popup at a world position showing the given amount.</summary>
        public static void Spawn(DamagePopup prefab, Vector3 worldPos, float amount)
        {
            if (prefab == null) return;

            Camera worldCamera = Camera.main;
            if (worldCamera == null) return;

            Vector2 scatter = Random.insideUnitCircle * prefab.scatterRadius;
            Vector3 spawnPosition =
                worldPos
                + Vector3.up * prefab.spawnHeight
                + worldCamera.transform.right * scatter.x
                + worldCamera.transform.up * scatter.y;

            var popup = Instantiate(prefab, spawnPosition, Quaternion.identity);
            popup.Initialize(amount);
        }

        public void SetAmount(float amount)
        {
            _text.text = Mathf.RoundToInt(amount).ToString();
        }

        private void Initialize(float amount)
        {
            SetAmount(amount);
            BillboardTo(Camera.main);
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

            Camera worldCamera = Camera.main;
            BillboardTo(worldCamera);

            float lifetimeAlpha = 1f - Mathf.Clamp01(_elapsed / lifetime);
            float targetVisibility = IsBlockedByWall(worldCamera) ? 0f : 1f;
            _visibility = Mathf.MoveTowards(
                _visibility,
                targetVisibility,
                wallFadeSpeed * Time.deltaTime
            );

            var c = _baseColor;
            c.a = lifetimeAlpha * _visibility;
            _text.color = c;

            if (_elapsed >= lifetime)
                Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial != null)
                Destroy(_runtimeMaterial);
        }

        private void BillboardTo(Camera worldCamera)
        {
            if (worldCamera == null)
                return;

            transform.rotation = Quaternion.LookRotation(
                transform.position - worldCamera.transform.position,
                worldCamera.transform.up
            );
        }

        private bool IsBlockedByWall(Camera worldCamera)
        {
            if (worldCamera == null)
                return false;

            if (_wallMask < 0)
                _wallMask = LayerMask.GetMask("Wall");

            return Physics.Linecast(
                worldCamera.transform.position,
                transform.position,
                _wallMask,
                QueryTriggerInteraction.Ignore
            );
        }

        private void ApplyOverlayMaterial()
        {
            Shader overlayShader = Shader.Find(OverlayShaderName);
            if (overlayShader == null)
                return;

            Material source = _text.fontSharedMaterial != null
                ? _text.fontSharedMaterial
                : _text.fontMaterial;
            _runtimeMaterial = source != null ? new Material(source) : new Material(overlayShader);
            _runtimeMaterial.shader = overlayShader;
            _text.fontMaterial = _runtimeMaterial;
        }
    }
}
