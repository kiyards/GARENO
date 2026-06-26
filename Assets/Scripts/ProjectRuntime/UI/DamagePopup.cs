using TMPro;
using UnityEngine;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Non-networked floating damage number. Instantiated client-side at a world position,
    /// floats upward and fades out, then self-destructs.
    /// </summary>
    [RequireComponent(typeof(TextMeshPro))]
    public class DamagePopup : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.7f;
        [SerializeField] private float riseSpeed = 1.5f;

        private TextMeshPro _text;
        private float _elapsed;
        private Color _baseColor;

        private void Awake()
        {
            _text = GetComponent<TextMeshPro>();
            _baseColor = _text.color;
        }

        /// <summary>Spawn a popup at a world position showing the given amount.</summary>
        public static void Spawn(DamagePopup prefab, Vector3 worldPos, float amount)
        {
            if (prefab == null) return;
            var popup = Instantiate(prefab, worldPos, Quaternion.identity);
            popup.SetAmount(amount);
        }

        public void SetAmount(float amount)
        {
            if (_text == null) _text = GetComponent<TextMeshPro>();
            _text.text = Mathf.RoundToInt(amount).ToString();
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            transform.position += Vector3.up * (riseSpeed * Time.deltaTime);

            // Always face the active camera.
            if (Camera.main != null)
                transform.rotation = Camera.main.transform.rotation;

            float t = Mathf.Clamp01(_elapsed / lifetime);
            var c = _baseColor;
            c.a = Mathf.Lerp(1f, 0f, t);
            _text.color = c;

            if (_elapsed >= lifetime)
                Destroy(gameObject);
        }
    }
}
