using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class UIAttackDisplay : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Image AttackImage;
        [SerializeField] private Image AttackControlImage;
        [SerializeField] private Image AttackCooldownFillImage;
        [SerializeField] private TextMeshProUGUI AttackNameTMP;
        [SerializeField] private GameObject DarkenedOverlay;
    }
}