using Mirror;
using System;
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

            currentHealth = Mathf.Clamp(currentHealth - amount, 0f, maxHealth);
            OnDamagedEvent?.Invoke(amount, sourceNetId, hitPoint);

            if (currentHealth <= 0f)
            {
                _isDead = true;
                OnDeathEvent?.Invoke(sourceNetId);
            }
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
    }
}
