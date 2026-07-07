using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    // Full-screen red vignette that flashes when the local player takes damage, then fades out.
    // Only ever driven for the local player (PlayerHudManager binds it to the local Health).
    // Modeled on FlashEffect: a stretched Image whose alpha is animated by a restart-on-retrigger
    // coroutine. The Image's colour (the red tint) is authored in the prefab; this only drives alpha.
    public class DamageVignette : MonoBehaviour
    {
        [SerializeField] private Image vignetteImage;
        [SerializeField] private float peakAlpha = 0.7f;
        [SerializeField] private float fadeDuration = 0.5f;

        private Coroutine _activeFlash;

        public void Flash()
        {
            if (vignetteImage == null) return;
            if (_activeFlash != null) StopCoroutine(_activeFlash);
            _activeFlash = StartCoroutine(FlashCoroutine());
        }

        private IEnumerator FlashCoroutine()
        {
            SetAlpha(peakAlpha);
            float elapsed = 0f;
            while (elapsed < fadeDuration)
            {
                elapsed += Time.deltaTime;
                SetAlpha(peakAlpha * (1f - Mathf.Clamp01(elapsed / fadeDuration)));
                yield return null;
            }
            SetAlpha(0f);
            _activeFlash = null;
        }

        private void SetAlpha(float alpha)
        {
            if (vignetteImage == null) return;
            Color c = vignetteImage.color;
            c.a = alpha;
            vignetteImage.color = c;
        }
    }
}
