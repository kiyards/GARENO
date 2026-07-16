using Mirror;
using System;
using ProjectRuntime.Actor;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using ProjectRuntime.UI;
using UnityEngine;

namespace ProjectRuntime.Combat
{
    /// <summary>
    /// Generic, reusable health/damage component. Server is authoritative over
    /// <see cref="currentHealth"/>; clients react via the SyncVar hook.
    /// Reused verbatim on survivors, crystals, enemies and traps.
    /// </summary>
    public class Health : NetworkBehaviour, IDamageable
    {
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private DamagePopup damagePopupPrefab;

        [SyncVar(hook = nameof(OnHealthSynced))]
        private float currentHealth;

        /// <summary>(current, max) — raised on every peer when health changes. UI subscribes.</summary>
        public event Action<float, float> OnHealthChangedEvent;
        public event Action<float, uint, Vector3> OnDamagedEvent;

        /// <summary>Server-only. Raised once when health reaches zero. Arg is the killer's netId.</summary>
        public event Action<uint> OnDeathEvent;

        private bool _isDead;
        private bool _damageEnabled = true;

        public float MaxHealth => maxHealth;
        public float CurrentHealth => currentHealth;
        public bool IsAlive => !_isDead && currentHealth > 0f;
        public bool IsDamageEnabled => _damageEnabled;

        public void ConfigureMaxHealth(float value)
        {
            maxHealth = Mathf.Max(1f, value);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            currentHealth = maxHealth;
            _isDead = false;
            _damageEnabled = true;
        }

        [Server]
        public void ServerTakeDamage(float amount, uint sourceNetId, Vector3 hitPoint)
        {
            if (_isDead || !_damageEnabled || amount <= 0f) return;

            float appliedDamage = Mathf.Min(amount, currentHealth);
            currentHealth = Mathf.Clamp(currentHealth - amount, 0f, maxHealth);
            OnDamagedEvent?.Invoke(appliedDamage, sourceNetId, hitPoint);
            RpcShowDamageNumber(hitPoint, appliedDamage, netId);
            this.ReportSurvivorDamageToBattleManager(appliedDamage, sourceNetId);

            if (currentHealth <= 0f)
            {
                _isDead = true;
                OnDeathEvent?.Invoke(sourceNetId);
            }
        }

        [Server]
        public void ServerHeal(float amount)
        {
            if (_isDead || amount <= 0f || currentHealth >= maxHealth)
            {
                return;
            }

            currentHealth = Mathf.Clamp(currentHealth + amount, 0f, maxHealth);
        }

        [Server]
        public void ServerSetDamageEnabled(bool enabled)
        {
            _damageEnabled = enabled;
        }

        /// <summary>Server-only. Restore to full (e.g. on respawn).</summary>
        [Server]
        public void ServerResetHealth()
        {
            currentHealth = maxHealth;
            _isDead = false;
            _damageEnabled = true;
        }

        private void OnHealthSynced(float oldValue, float newValue)
        {
            OnHealthChangedEvent?.Invoke(newValue, maxHealth);
        }

        [ClientRpc]
        private void RpcShowDamageNumber(Vector3 worldPos, float amount, uint damagedNetId)
        {
            if (!ShouldShowDamageNumber(damagedNetId))
            {
                return;
            }

            DamagePopup.Spawn(damagePopupPrefab, worldPos, amount);
        }

        private bool ShouldShowDamageNumber(uint damagedNetId)
        {
            var localManager = PlayerManager.Instance;
            var localPlayer = localManager != null ? localManager.player : null;
            if (localManager == null || localPlayer == null)
            {
                return false;
            }

            if (localPlayer.netId == damagedNetId)
            {
                return false;
            }

            if (localManager.playerRole == PlayerRole.Survivor)
            {
                return true;
            }

            if (localManager.playerRole != PlayerRole.DungeonMaster)
            {
                return false;
            }

            if (!IsLocalDungeonMasterInTurretMode(localPlayer))
            {
                return false;
            }

            var damagedTurret = GetComponent<DungeonMasterTurret>();
            return damagedTurret == null || damagedTurret.OwnerNetId != localPlayer.netId;
        }

        private static bool IsLocalDungeonMasterInTurretMode(GameplayPlayer localPlayer)
        {
            return localPlayer.currentState is DungeonMasterTurretState
                || localPlayer.nextState is DungeonMasterTurretState;
        }

        [Server]
        private void ReportSurvivorDamageToBattleManager(float damageAmount, uint sourceNetId)
        {
            if (damageAmount <= 0f)
            {
                return;
            }

            var gameplayPlayer = GetComponentInParent<GameplayPlayer>();
            var playerManager = gameplayPlayer != null ? gameplayPlayer.localManager : null;
            if (playerManager == null || playerManager.playerRole != PlayerRole.Survivor)
            {
                return;
            }

            BattleManager.Instance?.ServerReportSurvivorDamaged(
                playerManager,
                damageAmount,
                sourceNetId
            );
        }
    }
}
