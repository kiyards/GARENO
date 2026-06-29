using Mirror;
using ProjectRuntime.Combat;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectRuntime.Actor
{
    public class ZombieEnemy : NetworkBehaviour
    {
        [Header("Stats")]
        [SerializeField] private float moveSpeed = 2.4f;   // 0.8x survivor moveSpeed (3f)
        [SerializeField] private float damage = 30f;
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private float attackCooldown = 1f;

        private Health _health;
        private NavMeshAgent _agent;
        private float _attackTimer;

        private void Awake()
        {
            this._health = this.GetComponent<Health>();
            this._agent = this.GetComponent<NavMeshAgent>();
            this._agent.speed = this.moveSpeed;
        }

        public override void OnStartServer()
        {
            this._health.OnDeathEvent += this.OnServerDeath;
        }

        public override void OnStopServer()
        {
            this._health.OnDeathEvent -= this.OnServerDeath;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();

            // Movement is server-authoritative and replicated via NetworkTransform. On
            // clients the agent must not drive the transform, or it fights the synced
            // position and the zombie appears frozen at its spawn point. (Host is also the
            // server, so its agent stays enabled and remains the source of truth.)
            if (!this.isServer && this._agent != null)
            {
                this._agent.enabled = false;
            }
        }

        private void FixedUpdate()
        {
            if (!this.isServer) return;
            this.ServerTick();
        }

        [Server]
        private void ServerTick()
        {
            GameplayPlayer target = this.FindNearestAliveSurvivor();
            if (target == null)
            {
                this._agent.ResetPath();
                return;
            }

            this._agent.SetDestination(target.transform.position);

            this._attackTimer -= Time.fixedDeltaTime;
            float dist = Vector3.Distance(this.transform.position, target.transform.position);
            if (dist <= this.attackRange && this._attackTimer <= 0f)
            {
                target.health.ServerTakeDamage(this.damage, this.netId, this.transform.position);
                this._attackTimer = this.attackCooldown;
            }
        }

        [Server]
        private GameplayPlayer FindNearestAliveSurvivor()
        {
            if (BattleManager.Instance == null) return null;

            GameplayPlayer nearest = null;
            float minDist = float.MaxValue;

            foreach (PlayerManager pm in BattleManager.Instance.Players)
            {
                if (pm == null || pm.playerRole != PlayerRole.Survivor) continue;
                if (pm.player == null ||
                    pm.player.IsInactive ||
                    pm.player.health == null ||
                    !pm.player.health.IsAlive) continue;

                float d = Vector3.Distance(this.transform.position, pm.player.transform.position);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = pm.player;
                }
            }

            return nearest;
        }

        [Server]
        private void OnServerDeath(uint killerNetId)
        {
            NetworkServer.Destroy(this.gameObject);
        }
    }
}
