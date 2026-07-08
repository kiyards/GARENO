using System.Collections.Generic;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
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
        [SerializeField] private TextMeshProUGUI DownedTimerTMP;
        [SerializeField] private GameObject DungeonMasterObject;

        [Header("Status Colours")]
        [SerializeField] private Color HealthySurvivorColor;
        [SerializeField] private Color WarningSurvivorColor;
        [SerializeField] private Color DownedSurvivorColor;
        [SerializeField] private Color DungeonMasterColor = Color.red;

        private PlayerManager _boundPlayer;
        private Health _boundHealth;
        private float _nextRefreshTime;

        private void Awake()
        {
            CacheOptionalReferences();
            Refresh();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Update()
        {
            if (_boundPlayer == null)
            {
                return;
            }

            if (Time.unscaledTime < _nextRefreshTime)
            {
                return;
            }

            _nextRefreshTime = Time.unscaledTime + 0.1f;
            BindHealthIfNeeded();
            Refresh();
        }

        public void Bind(PlayerManager player)
        {
            if (_boundPlayer == player)
            {
                Refresh();
                return;
            }

            Unsubscribe();
            _boundPlayer = player;

            if (_boundPlayer != null)
            {
                _boundPlayer.OnPlayerNameChanged += OnPlayerNameChanged;
                _boundPlayer.OnPlayerRoleChanged += OnPlayerRoleChanged;
            }

            BindHealthIfNeeded();
            Refresh();
        }

        public void Unbind()
        {
            Unsubscribe();
            Refresh();
        }

        private void Unsubscribe()
        {
            if (_boundPlayer != null)
            {
                _boundPlayer.OnPlayerNameChanged -= OnPlayerNameChanged;
                _boundPlayer.OnPlayerRoleChanged -= OnPlayerRoleChanged;
            }

            if (_boundHealth != null)
            {
                _boundHealth.OnHealthChangedEvent -= OnHealthChanged;
            }

            _boundPlayer = null;
            _boundHealth = null;
        }

        private void BindHealthIfNeeded()
        {
            var nextHealth = _boundPlayer != null && _boundPlayer.player != null
                ? _boundPlayer.player.health
                : null;

            if (_boundHealth == nextHealth)
            {
                return;
            }

            if (_boundHealth != null)
            {
                _boundHealth.OnHealthChangedEvent -= OnHealthChanged;
            }

            _boundHealth = nextHealth;
            if (_boundHealth != null)
            {
                _boundHealth.OnHealthChangedEvent += OnHealthChanged;
            }
        }

        private void OnPlayerNameChanged(string playerName) => Refresh();
        private void OnPlayerRoleChanged(PlayerRole role) => Refresh();
        private void OnHealthChanged(float current, float max) => Refresh();

        private void Refresh()
        {
            CacheOptionalReferences();

            if (_boundPlayer == null)
            {
                gameObject.SetActive(false);
                return;
            }

            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
            }

            if (PlayerNameTMP != null)
            {
                PlayerNameTMP.text = string.IsNullOrWhiteSpace(_boundPlayer.playerName)
                    ? $"Player {_boundPlayer.playerIndex}"
                    : _boundPlayer.playerName;
            }

            bool isDungeonMaster = _boundPlayer.playerRole == PlayerRole.DungeonMaster;
            bool isDowned = _boundPlayer.player != null && _boundPlayer.player.IsDowned;

            if (DownedPlayerObject != null)
            {
                DownedPlayerObject.SetActive(isDowned);
            }

            if (DownedTimerTMP != null)
            {
                DownedTimerTMP.text = isDowned
                    ? $"{_boundPlayer.player.DownedTimeRemaining:0.0}s"
                    : string.Empty;
            }

            if (DungeonMasterObject != null)
            {
                DungeonMasterObject.SetActive(isDungeonMaster);
            }

            RefreshStatusColor(isDungeonMaster, isDowned);
        }

        private void RefreshStatusColor(bool isDungeonMaster, bool isDowned)
        {
            if (PlayerStatusImage == null)
            {
                return;
            }

            if (isDungeonMaster)
            {
                PlayerStatusImage.color = DungeonMasterColor;
                return;
            }

            if (isDowned || _boundHealth == null)
            {
                PlayerStatusImage.color = DownedSurvivorColor;
                return;
            }

            float healthPercent = _boundHealth.MaxHealth > 0f
                ? _boundHealth.CurrentHealth / _boundHealth.MaxHealth
                : 0f;
            PlayerStatusImage.color = healthPercent > 0.5f
                ? HealthySurvivorColor
                : WarningSurvivorColor;
        }

        private void CacheOptionalReferences()
        {
            if (DungeonMasterObject == null)
            {
                var dungeonMasterTransform = transform.Find("DungeonMasterOverlay");
                if (dungeonMasterTransform != null)
                {
                    DungeonMasterObject = dungeonMasterTransform.gameObject;
                }
            }
        }
    }
}
