using ProjectRuntime.Actor;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    // One of the 3 fixed Nemesis attack icons (Punch/Lunge/Ground Slam). Unlike the rotating card
    // hand, these abilities never change, so each prefab instance is tagged in the Inspector with
    // the ability it represents and PlayerHudManager just refreshes all 3 every frame.
    public class UIAttackDisplay : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private NemesisAttackType AttackType;

        [Header("Scene References")]
        [SerializeField] private Image AttackImage;
        [SerializeField] private Image AttackControlImage;
        [SerializeField] private Image AttackCooldownFillImage;
        [SerializeField] private TextMeshProUGUI AttackNameTMP;
        [SerializeField] private GameObject DarkenedOverlay;

        public NemesisAttackType Type => AttackType;

        // fraction: 0 = just used (full cooldown remaining) → 1 = ready. Matches
        // DungeonMasterNemesis.GetAttackCooldownFraction so callers can pass it straight through.
        public void SetCooldownFill(float fraction)
        {
            if (AttackCooldownFillImage != null)
            {
                AttackCooldownFillImage.fillAmount = Mathf.Clamp01(fraction);
            }
        }

        public void SetAvailable(bool available)
        {
            if (DarkenedOverlay != null)
            {
                DarkenedOverlay.SetActive(!available);
            }
        }
    }
}