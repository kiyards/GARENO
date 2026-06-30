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
        private enum ZombieAiState
        {
            Spawning,
            Wandering,
            Chasing,
            Attacking,
        }

        [Header("Stats")]
        [SerializeField] private float moveSpeed = 2.4f;   // 0.8x survivor moveSpeed (3f)
        [SerializeField] private float damage = 30f;
        [SerializeField] private float attackRange = 1.5f;
        [SerializeField] private float attackCooldown = 1f;

        [Header("Spawn")]
        [SerializeField] private float spawnWarmupDuration = 1f;

        [Header("Awareness")]
        [SerializeField] private float detectionRadius = 10f;
        [SerializeField] private float wanderRadius = 6f;
        [SerializeField] private float wanderPointReachedDistance = 0.5f;
        [SerializeField] private float wanderPauseDuration = 1.25f;

        [Header("Lunge")]
        [SerializeField] private float lungeDistance = 1.2f;
        [SerializeField] private float lungeDuration = 0.18f;

        [SyncVar(hook = nameof(OnTargetableSynced))]
        private bool isTargetable = true;

        private Health _health;
        private NavMeshAgent _agent;
        private Collider[] _hitColliders;
        private bool[] _hitColliderInitialEnabled;
        private ZombieAiState _state = ZombieAiState.Spawning;
        private GameplayPlayer _target;
        private Vector3 _spawnPosition;
        private double _spawnWarmupEndTime;
        private double _nextWanderMoveTime;
        private double _nextAttackTime;
        private double _attackStartTime;
        private Vector3 _lungeStartPosition;
        private Vector3 _lungeEndPosition;
        private bool _attackDamageApplied;
        private bool _hasWanderDestination;

        private void Awake()
        {
            this.CacheComponents();
            this.ConfigureAgent();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            this.CacheComponents();
            this.ConfigureAgent();
            this._spawnPosition = this.transform.position;
            this._state = ZombieAiState.Spawning;
            this._target = null;
            this._spawnWarmupEndTime = NetworkTime.time + Mathf.Max(0f, this.spawnWarmupDuration);
            this._nextWanderMoveTime = 0d;
            this._nextAttackTime = 0d;
            this._hasWanderDestination = false;
            this.ServerSetTargetable(this.spawnWarmupDuration <= 0f);

            this._health.OnDeathEvent += this.OnServerDeath;
            this._health.OnDamagedEvent += this.OnServerDamaged;
        }

        public override void OnStopServer()
        {
            if (this._health != null)
            {
                this._health.OnDeathEvent -= this.OnServerDeath;
                this._health.OnDamagedEvent -= this.OnServerDamaged;
            }

            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            this.CacheComponents();
            this.ApplyTargetable(this.isTargetable);

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
        protected virtual void ServerTick()
        {
            if (this._health == null || !this._health.IsAlive)
            {
                return;
            }

            switch (this._state)
            {
                case ZombieAiState.Spawning:
                    this.ServerTickSpawning();
                    return;

                case ZombieAiState.Attacking:
                    this.ServerTickAttack();
                    return;
            }

            if (this._target != null && !this.IsValidTarget(this._target))
            {
                this._target = null;
                this.ServerEnterWandering();
            }

            if (this._target == null)
            {
                this._target = this.FindNearestDetectableSurvivor();
                if (this._target != null)
                {
                    this._state = ZombieAiState.Chasing;
                }
            }

            if (this._target != null)
            {
                this.ServerTickChasing();
                return;
            }

            this.ServerTickWandering();
        }

        [Server]
        private void ServerTickSpawning()
        {
            this.StopAgent();

            if (NetworkTime.time < this._spawnWarmupEndTime)
            {
                return;
            }

            this.ServerSetTargetable(true);
            this.ServerEnterWandering();
        }

        [Server]
        private void ServerTickWandering()
        {
            if (!this.IsAgentReady())
            {
                return;
            }

            if (this._agent.pathPending)
            {
                return;
            }

            if (this._hasWanderDestination)
            {
                if (this._agent.hasPath &&
                    this._agent.remainingDistance > this.wanderPointReachedDistance)
                {
                    return;
                }

                this._hasWanderDestination = false;
                this._nextWanderMoveTime = NetworkTime.time + Mathf.Max(0f, this.wanderPauseDuration);
                this.StopAgent();
                return;
            }

            if (NetworkTime.time < this._nextWanderMoveTime)
            {
                this.StopAgent();
                return;
            }

            if (this.TryGetWanderPoint(out Vector3 point))
            {
                this._agent.isStopped = false;
                this._agent.SetDestination(point);
                this._hasWanderDestination = true;
            }
            else
            {
                this._nextWanderMoveTime = NetworkTime.time + Mathf.Max(0.25f, this.wanderPauseDuration);
            }
        }

        [Server]
        private void ServerTickChasing()
        {
            if (!this.IsAgentReady())
            {
                return;
            }

            float distance = Vector3.Distance(this.transform.position, this._target.transform.position);
            if (distance <= this.attackRange && NetworkTime.time >= this._nextAttackTime)
            {
                this.ServerBeginAttack(this._target);
                return;
            }

            this._agent.isStopped = false;
            this._agent.SetDestination(this._target.transform.position);
        }

        [Server]
        protected virtual void ServerBeginAttack(GameplayPlayer target)
        {
            if (!this.IsValidTarget(target))
            {
                this.ServerEnterWandering();
                return;
            }

            this._state = ZombieAiState.Attacking;
            this._attackStartTime = NetworkTime.time;
            this._attackDamageApplied = false;
            this._lungeStartPosition = this.transform.position;

            Vector3 direction = target.transform.position - this.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = this.transform.forward;
            }

            this._lungeEndPosition = this._lungeStartPosition + direction.normalized * Mathf.Max(0f, this.lungeDistance);
            this.StopAgent();
        }

        [Server]
        private void ServerTickAttack()
        {
            float safeDuration = Mathf.Max(0.01f, this.lungeDuration);
            float progress = Mathf.Clamp01((float)((NetworkTime.time - this._attackStartTime) / safeDuration));
            Vector3 nextPosition = Vector3.Lerp(this._lungeStartPosition, this._lungeEndPosition, progress);
            this.MoveAgentTo(nextPosition);

            if (!this._attackDamageApplied && progress >= 0.5f)
            {
                this._attackDamageApplied = true;
                if (this.IsValidTarget(this._target) &&
                    Vector3.Distance(this.transform.position, this._target.transform.position) <= this.attackRange)
                {
                    this.ServerAttack(this._target);
                }
            }

            if (progress < 1f)
            {
                return;
            }

            this._nextAttackTime = NetworkTime.time + Mathf.Max(0f, this.attackCooldown);
            this._state = this.IsValidTarget(this._target)
                ? ZombieAiState.Chasing
                : ZombieAiState.Wandering;
        }

        // The action taken when a survivor comes within attackRange. Basic zombies deal melee
        // damage; subclasses (e.g. the creeper) override this to do something else.
        [Server]
        protected virtual void ServerAttack(GameplayPlayer target)
        {
            target.health.ServerTakeDamage(this.damage, this.netId, this.transform.position);
        }

        [Server]
        protected GameplayPlayer FindNearestDetectableSurvivor()
        {
            if (BattleManager.Instance == null) return null;

            GameplayPlayer nearest = null;
            float maxSqrDistance = this.detectionRadius * this.detectionRadius;
            float minSqrDistance = maxSqrDistance;

            foreach (PlayerManager pm in BattleManager.Instance.Players)
            {
                GameplayPlayer candidate = pm != null ? pm.player : null;
                if (!this.IsValidTarget(candidate))
                {
                    continue;
                }

                float sqrDistance = (this.transform.position - candidate.transform.position).sqrMagnitude;
                if (sqrDistance <= minSqrDistance)
                {
                    minSqrDistance = sqrDistance;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        [Server]
        private void OnServerDamaged(float amount, uint sourceNetId, Vector3 hitPoint)
        {
            if (this._state == ZombieAiState.Spawning)
            {
                return;
            }

            if (this.TryGetSurvivorFromNetId(sourceNetId, out GameplayPlayer source))
            {
                this._target = source;
                this._state = ZombieAiState.Chasing;
            }
        }

        [Server]
        private void OnServerDeath(uint killerNetId)
        {
            BattleManager.Instance?.ServerReportZombieKilled(this, killerNetId);
            NetworkServer.Destroy(this.gameObject);
        }

        private void CacheComponents()
        {
            this._health ??= this.GetComponent<Health>();
            this._agent ??= this.GetComponent<NavMeshAgent>();

            if (this._hitColliders == null || this._hitColliders.Length == 0)
            {
                this._hitColliders = this.GetComponentsInChildren<Collider>(true);
                this._hitColliderInitialEnabled = new bool[this._hitColliders.Length];
                for (int i = 0; i < this._hitColliders.Length; i++)
                {
                    this._hitColliderInitialEnabled[i] = this._hitColliders[i] != null && this._hitColliders[i].enabled;
                }
            }
        }

        private void ConfigureAgent()
        {
            if (this._agent != null)
            {
                this._agent.speed = this.moveSpeed;
            }
        }

        private bool IsAgentReady()
        {
            return this._agent != null && this._agent.enabled && this._agent.isOnNavMesh;
        }

        private void StopAgent()
        {
            if (!this.IsAgentReady())
            {
                return;
            }

            this._agent.isStopped = true;
            this._agent.ResetPath();
        }

        private void MoveAgentTo(Vector3 nextPosition)
        {
            if (!this.IsAgentReady())
            {
                this.transform.position = nextPosition;
                return;
            }

            this._agent.Move(nextPosition - this.transform.position);
        }

        [Server]
        private void ServerEnterWandering()
        {
            this._state = ZombieAiState.Wandering;
            this._target = null;
            this._hasWanderDestination = false;
            this._nextWanderMoveTime = NetworkTime.time + Mathf.Max(0f, this.wanderPauseDuration);
            this.StopAgent();
        }

        [Server]
        private bool TryGetWanderPoint(out Vector3 point)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector2 offset = Random.insideUnitCircle * this.wanderRadius;
                Vector3 candidate = this._spawnPosition + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, this.wanderRadius, NavMesh.AllAreas))
                {
                    point = hit.position;
                    return true;
                }
            }

            point = this._spawnPosition;
            return NavMesh.SamplePosition(
                this._spawnPosition,
                out NavMeshHit spawnHit,
                this.wanderRadius,
                NavMesh.AllAreas);
        }

        private bool IsValidTarget(GameplayPlayer player)
        {
            return player != null &&
                   player.localManager != null &&
                   player.localManager.playerRole == PlayerRole.Survivor &&
                   !player.IsInactive &&
                   player.health != null &&
                   player.health.IsAlive;
        }

        [Server]
        private bool TryGetSurvivorFromNetId(uint sourceNetId, out GameplayPlayer player)
        {
            player = null;
            if (sourceNetId == 0 ||
                !NetworkServer.spawned.TryGetValue(sourceNetId, out NetworkIdentity identity))
            {
                return false;
            }

            PlayerManager manager = identity.GetComponentInParent<PlayerManager>()
                                    ?? identity.GetComponentInChildren<PlayerManager>();
            player = manager != null ? manager.player : null;
            return this.IsValidTarget(player);
        }

        [Server]
        private void ServerSetTargetable(bool targetable)
        {
            this.isTargetable = targetable;
            this.ApplyTargetable(targetable);
            this._health?.ServerSetDamageEnabled(targetable);
        }

        private void OnTargetableSynced(bool oldValue, bool newValue)
        {
            this.ApplyTargetable(newValue);
        }

        private void ApplyTargetable(bool targetable)
        {
            this.CacheComponents();

            if (this._hitColliders == null || this._hitColliderInitialEnabled == null)
            {
                return;
            }

            for (int i = 0; i < this._hitColliders.Length; i++)
            {
                if (this._hitColliders[i] == null)
                {
                    continue;
                }

                this._hitColliders[i].enabled = targetable && this._hitColliderInitialEnabled[i];
            }
        }
    }
}
