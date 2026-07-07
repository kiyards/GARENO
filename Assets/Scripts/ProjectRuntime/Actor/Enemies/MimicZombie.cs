using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using TMPro;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    /// <summary>
    /// A zombie disguised as a survivor: it carries a randomized player name overhead. Its AI is
    /// identical to <see cref="ZombieEnemy"/> (wander, chase, lunge, melee) — per the GDD the only
    /// difference is the disguise. The displayed name is chosen on the server (from the connected
    /// survivors, falling back to a built-in list when none are available) and replicated so every
    /// client shows the same name.
    /// </summary>
    public class MimicZombie : ZombieEnemy
    {
        [Header("Mimic")]
        [SerializeField] private TextMeshProUGUI nameText;

        // Used only when no survivor name is available (e.g. the mimic is spawned before any
        // survivor identity has synced to the server).
        private static readonly string[] FallbackNames =
        {
            "Alex", "Sam", "Jordan", "Casey", "Riley", "Morgan",
        };

        [SyncVar(hook = nameof(OnMimicNameSynced))]
        private string mimicName;

        public override void OnStartServer()
        {
            base.OnStartServer();
            this.ServerApplyMimicTarget(this.ServerPickMimicTarget());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            this.ApplyName(this.mimicName);
        }

        // Chooses the survivor this mimic is disguised as. Returns null when there is no survivor to
        // copy (e.g. the mimic is spawned before any survivor identity has synced), in which case the
        // caller falls back to a random built-in name.
        [Server]
        private PlayerManager ServerPickMimicTarget()
        {
            List<PlayerManager> candidates = new();
            if (BattleManager.Instance != null)
            {
                foreach (PlayerManager pm in BattleManager.Instance.Players)
                {
                    if (pm != null &&
                        pm.playerRole == PlayerRole.Survivor &&
                        !string.IsNullOrWhiteSpace(pm.playerName))
                    {
                        candidates.Add(pm);
                    }
                }
            }

            return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
        }

        // Snapshots the copied survivor's identity into replicated state. We snapshot (rather than
        // hold a live reference) so the disguise survives the copied player disconnecting mid-match.
        [Server]
        private void ServerApplyMimicTarget(PlayerManager target)
        {
            this.mimicName = target != null
                ? target.playerName
                : FallbackNames[Random.Range(0, FallbackNames.Length)];
        }

        private void OnMimicNameSynced(string oldValue, string newValue)
        {
            this.ApplyName(newValue);
        }

        private void ApplyName(string value)
        {
            if (this.nameText != null)
            {
                this.nameText.text = value;
            }
        }
    }
}
