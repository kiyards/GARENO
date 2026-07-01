using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.UI
{
    public class UIPlayerVitalsPanel : MonoBehaviour
    {
        [SerializeField] private Transform rowsParent;
        [SerializeField] private UIPlayerVital rowPrefab;
        [SerializeField] private int maxRows = 5;

        private readonly List<UIPlayerVital> _rows = new();
        private BattleManager _boundBattleManager;
        private float _nextBindAttemptTime;
        private float _nextRefreshTime;

        private void Awake()
        {
            rowsParent ??= transform;
            RefreshRows();
        }

        private void OnEnable()
        {
            TryBindBattleManager();
            RefreshRows();
        }

        private void OnDisable()
        {
            UnbindBattleManager();
            ClearRows();
        }

        private void Update()
        {
            if (_boundBattleManager == null && Time.unscaledTime >= _nextBindAttemptTime)
            {
                _nextBindAttemptTime = Time.unscaledTime + 0.25f;
                TryBindBattleManager();
            }

            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + 0.25f;
                RefreshRows();
            }
        }

        private void TryBindBattleManager()
        {
            if (_boundBattleManager == BattleManager.Instance)
            {
                return;
            }

            UnbindBattleManager();

            if (BattleManager.Instance == null)
            {
                return;
            }

            _boundBattleManager = BattleManager.Instance;
            _boundBattleManager.Players.OnChange += OnPlayersChanged;
            _boundBattleManager.OnRoundStateChanged += OnRoundStateChanged;
            RefreshRows();
        }

        private void UnbindBattleManager()
        {
            if (_boundBattleManager == null)
            {
                return;
            }

            _boundBattleManager.Players.OnChange -= OnPlayersChanged;
            _boundBattleManager.OnRoundStateChanged -= OnRoundStateChanged;
            _boundBattleManager = null;
        }

        private void OnPlayersChanged(SyncList<PlayerManager>.Operation op, int index, PlayerManager item)
            => RefreshRows();

        private void OnRoundStateChanged() => RefreshRows();

        private void RefreshRows()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_boundBattleManager == null)
            {
                ClearRows();
                return;
            }

            var orderedPlayers = GetOrderedPlayers();
            EnsureRowCount(Mathf.Min(orderedPlayers.Count, Mathf.Max(0, maxRows)));

            for (int i = 0; i < _rows.Count; i++)
            {
                if (i < orderedPlayers.Count && i < maxRows)
                {
                    _rows[i].Bind(orderedPlayers[i]);
                }
                else
                {
                    _rows[i].Unbind();
                }
            }
        }

        private List<PlayerManager> GetOrderedPlayers()
        {
            var players = new List<PlayerManager>();
            if (_boundBattleManager == null)
            {
                return players;
            }

            foreach (var player in _boundBattleManager.Players)
            {
                if (player != null && player.playerRole != PlayerRole.DungeonMaster)
                {
                    players.Add(player);
                }
            }

            players.Sort(ComparePlayers);

            var dungeonMasters = new List<PlayerManager>();
            foreach (var player in _boundBattleManager.Players)
            {
                if (player != null && player.playerRole == PlayerRole.DungeonMaster)
                {
                    dungeonMasters.Add(player);
                }
            }

            dungeonMasters.Sort(ComparePlayers);
            players.AddRange(dungeonMasters);
            return players;
        }

        private static int ComparePlayers(PlayerManager a, PlayerManager b)
        {
            int indexCompare = a.playerIndex.CompareTo(b.playerIndex);
            if (indexCompare != 0)
            {
                return indexCompare;
            }

            return a.netId.CompareTo(b.netId);
        }

        private void EnsureRowCount(int requiredCount)
        {
            if (rowPrefab == null || rowsParent == null)
            {
                return;
            }

            while (_rows.Count < requiredCount)
            {
                var row = Instantiate(rowPrefab, rowsParent);
                row.gameObject.SetActive(false);
                _rows.Add(row);
            }
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
            {
                if (row != null)
                {
                    row.Unbind();
                }
            }
        }
    }
}
