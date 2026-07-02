using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class FlashEffect : MonoBehaviour
    {
        [SerializeField] private Image flashImage;
        [SerializeField] private float peakHoldDuration = 0.3f;

        private Coroutine _activeFlash;

        public void StartFlash(float duration, float peakAlpha = 1f)
        {
            if (flashImage == null) return;
            if (_activeFlash != null) StopCoroutine(_activeFlash);
            _activeFlash = StartCoroutine(FlashCoroutine(duration, Mathf.Clamp01(peakAlpha)));
        }

        private IEnumerator FlashCoroutine(float duration, float peakAlpha)
        {
            SetAlpha(peakAlpha);
            float hold = Mathf.Min(peakHoldDuration, duration);
            yield return new WaitForSeconds(hold);
            float fade = duration - hold;
            float elapsed = 0f;
            while (elapsed < fade)
            {
                elapsed += Time.deltaTime;
                SetAlpha(peakAlpha * (1f - Mathf.Clamp01(elapsed / fade)));
                yield return null;
            }
            SetAlpha(0f);
            _activeFlash = null;
        }

        private void SetAlpha(float alpha)
        {
            if (flashImage == null) return;
            Color c = flashImage.color;
            c.a = alpha;
            flashImage.color = c;
        }
    }
}
