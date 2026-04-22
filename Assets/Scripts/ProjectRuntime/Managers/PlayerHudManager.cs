using ProjectRuntime.UI;
using TMPro;
using UnityEngine;

namespace ProjectRuntime.Managers
{
    public class PlayerHudManager : MonoBehaviour
    {
        public static PlayerHudManager Instance { get; private set; }

        [field: SerializeField, Header("Scene References")]
        private GameObject UIParent { get; set; }

        [field: SerializeField, Header("Player Health")]
        private FlashFillBar PlayerHealthBar { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI PlayerHealthTMP { get; set; }

        private void Awake()
        {

        }

        public void TogglePlayerUI(bool toggle)
        {
            this.UIParent.SetActive(toggle);
        }
    }
}