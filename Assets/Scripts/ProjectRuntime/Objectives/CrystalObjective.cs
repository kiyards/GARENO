using Mirror;
using ProjectRuntime.Combat;
using ProjectRuntime.Managers;
using UnityEngine;

namespace ProjectRuntime.Objectives
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health))]
    public class CrystalObjective : NetworkBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Renderer[] renderers;
        [SerializeField] private Collider[] colliders;

        [SyncVar(hook = nameof(OnDespawnedSynced))]
        private bool isDespawned;

        [SyncVar]
        private bool isGuidanceRevealed;

        private bool _reportedDestroyed;

        public bool IsDespawned => isDespawned;
        public bool IsGuidanceRevealed => isGuidanceRevealed;

        private void Awake()
        {
            CacheComponents();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            CacheComponents();
            this._reportedDestroyed = false;
            this.isGuidanceRevealed = false;

            if (this.health != null)
            {
                this.health.OnDamagedEvent += this.OnDamaged;
                this.health.OnDeathEvent += this.OnHealthDepleted;
            }

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.ServerRegisterCrystal(this);
            }
        }

        public override void OnStopServer()
        {
            if (this.health != null)
            {
                this.health.OnDamagedEvent -= this.OnDamaged;
                this.health.OnDeathEvent -= this.OnHealthDepleted;
            }

            if (BattleManager.Instance != null)
            {
                BattleManager.Instance.ServerUnregisterCrystal(this);
            }

            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            CacheComponents();
            ApplyDespawned(this.isDespawned);
        }

        [Server]
        public void ServerDespawn()
        {
            if (this.isDespawned)
            {
                return;
            }

            this.isDespawned = true;
            ApplyDespawned(true);
        }

        [Server]
        private void OnDamaged(float amount, uint sourceNetId, Vector3 hitPoint)
        {
            BattleManager.Instance?.ServerReportCrystalDamaged(this, hitPoint);

            if (!this.isGuidanceRevealed)
            {
                this.isGuidanceRevealed = true;
            }
        }

        [Server]
        private void OnHealthDepleted(uint killerNetId)
        {
            if (this._reportedDestroyed)
            {
                return;
            }

            this._reportedDestroyed = true;
            BattleManager.Instance?.ServerReportCrystalDestroyed(this);
            ServerDespawn();
        }

        private void OnDespawnedSynced(bool oldValue, bool newValue)
        {
            ApplyDespawned(newValue);
        }

        private void CacheComponents()
        {
            this.health ??= GetComponent<Health>();

            if (this.renderers == null || this.renderers.Length == 0)
            {
                this.renderers = GetComponentsInChildren<Renderer>(true);
            }

            if (this.colliders == null || this.colliders.Length == 0)
            {
                this.colliders = GetComponentsInChildren<Collider>(true);
            }
        }

        private void ApplyDespawned(bool despawned)
        {
            CacheComponents();

            foreach (var targetRenderer in this.renderers)
            {
                if (targetRenderer != null)
                {
                    targetRenderer.enabled = !despawned;
                }
            }

            foreach (var targetCollider in this.colliders)
            {
                if (targetCollider != null)
                {
                    targetCollider.enabled = !despawned;
                }
            }
        }
    }
}
