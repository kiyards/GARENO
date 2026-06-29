using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class UIPlayerVital : MonoBehaviour
    {
        [Header("Scene References")]
        [SerializeField] private Image PlayerImage;
        [SerializeField] private TextMeshProUGUI PlayerNameTMP;
        [SerializeField] private Image PlayerStatusImage;
        [SerializeField] private GameObject DownedPlayerObject;
        [SerializeField] private GameObject DeadPlayerObject;
        [SerializeField] private List<GameObject> PlayerLivesIcons;

        [Header("Status Colours")]
        [SerializeField] private Color HealthySurvivorColor;
        [SerializeField] private Color WarningSurvivorColor;
        [SerializeField] private Color DownedSurvivorColor;
    }
}