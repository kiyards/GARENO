using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class FlashFillBar : MonoBehaviour
    {
        [field: SerializeField, Header("Scene References")]
        private Image FlashFill { get; set; }

        [field: SerializeField]
        private Image SolidFill { get; set; }

        [field: SerializeField, Header("Settings")]
        private float FlashFillSpeed { get; set; }

        private float _fillAmount;
        public float FillAmount
        {
            get { return this._fillAmount; }
            set
            {
                this._fillAmount = Mathf.Clamp01(value);
                if (Mathf.Approximately(this._fillAmount, this.SolidFill.fillAmount)) return;
                this.StopAllCoroutines();
                if (!gameObject.activeSelf) return;

                if (this._fillAmount < SolidFill.fillAmount)
                    this.StartCoroutine(this.DecreaseEffect());
                else if (this._fillAmount > SolidFill.fillAmount)
                    this.StartCoroutine(this.IncreaseEffect());
            }
        }

        private void Awake()
        {
            this._fillAmount = 1;
        }

        private IEnumerator DecreaseEffect()
        {
            this.SolidFill.fillAmount = this._fillAmount;
            while (this.FlashFill.fillAmount > this.SolidFill.fillAmount + 0.001f)
            {
                this.FlashFill.fillAmount = Mathf.Lerp(this.FlashFill.fillAmount, this.SolidFill.fillAmount,
                    this.FlashFillSpeed * Time.unscaledDeltaTime);
                yield return null;
            }
            this.FlashFill.fillAmount = this.SolidFill.fillAmount;
            yield break;
        }

        private IEnumerator IncreaseEffect()
        {
            this.FlashFill.fillAmount = this._fillAmount;
            while (this.SolidFill.fillAmount < this.FlashFill.fillAmount - 0.001f)
            {
                this.SolidFill.fillAmount = Mathf.Lerp(this.SolidFill.fillAmount, this.FlashFill.fillAmount,
                    this.FlashFillSpeed * Time.unscaledDeltaTime);
                yield return null;
            }
            this.SolidFill.fillAmount = this.FlashFill.fillAmount;
            yield break;
        }
    }
}