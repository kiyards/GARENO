using System.Collections;
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

        protected enum ZombieVisualState
        {
            Spawn,
            Idle,
            Walk,
            Lunge,
            Death,
            Explode,
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

        [Header("Facing")]
        [SerializeField] private float turnSpeed = 360f;
        [SerializeField] private float moveAlignmentAngle = 12f;

        [Header("Lunge")]
        [SerializeField] private float lungeDistance = 1.2f;
        [SerializeField] private float lungeDuration = 0.18f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController spawnController;
        [SerializeField] private RuntimeAnimatorController idleController;
        [SerializeField] private RuntimeAnimatorController walkController;
        [SerializeField] private RuntimeAnimatorController lungeController;
        [SerializeField] private RuntimeAnimatorController deathController;
        [SerializeField] private RuntimeAnimatorController explodeController;
        [SerializeField] private float attackAnimationHoldDuration = 0.6f;
        [SerializeField] private float deathDespawnDelay = 3.5f;

        [SyncVar(hook = nameof(OnTargetableSynced))]
        private bool isTargetable = true;

        [SyncVar(hook = nameof(OnVisualStateSynced))]
        private ZombieVisualState visualState = ZombieVisualState.Spawn;

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
        private Coroutine _deathDestroyCoroutine;

        private void Awake()
        {
            this.CacheComponents();
            this.ConfigureAgent();
            this.ApplyVisualState(this.visualState);
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
            this.ServerSetVisualState(ZombieVisualState.Spawn);

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
            this.ApplyVisualState(this.visualState);

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
                    this.ServerSetVisualState(ZombieVisualState.Walk);
                    if (!this.ServerFaceMovementTarget(this._agent.steeringTarget))
                    {
                        return;
                    }

                    this._agent.isStopped = false;
                    return;
                }

                this._hasWanderDestination = false;
                this._nextWanderMoveTime = NetworkTime.time + Mathf.Max(0f, this.wanderPauseDuration);
                this.StopAgent();
                this.ServerSetVisualState(ZombieVisualState.Idle);
                return;
            }

            if (NetworkTime.time < this._nextWanderMoveTime)
            {
                this.StopAgent();
                this.ServerSetVisualState(ZombieVisualState.Idle);
                return;
            }

            if (this.TryGetWanderPoint(out Vector3 point))
            {
                this._agent.isStopped = true;
                this._agent.SetDestination(point);
                this._hasWanderDestination = true;
                this.ServerSetVisualState(ZombieVisualState.Walk);
                this.ServerFaceMovementTarget(point);
            }
            else
            {
                this._nextWanderMoveTime = NetworkTime.time + Mathf.Max(0.25f, this.wanderPauseDuration);
                this.ServerSetVisualState(ZombieVisualState.Idle);
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

            this._agent.SetDestination(this._target.transform.position);
            this.ServerSetVisualState(ZombieVisualState.Walk);
            if (!this.ServerFaceMovementTarget(this._target.transform.position))
            {
                return;
            }

            this._agent.isStopped = false;
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
            this.ServerSetVisualState(ZombieVisualState.Lunge);

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
            double elapsed = NetworkTime.time - this._attackStartTime;
            float progress = Mathf.Clamp01((float)(elapsed / safeDuration));
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

            if (elapsed < Mathf.Max(safeDuration, this.attackAnimationHoldDuration))
            {
                this.StopAgent();
                return;
            }

            this._nextAttackTime = this._attackStartTime + safeDuration + Mathf.Max(0f, this.attackCooldown);
            if (this.IsValidTarget(this._target))
            {
                this._state = ZombieAiState.Chasing;
                this.ServerSetVisualState(ZombieVisualState.Walk);
            }
            else
            {
                this.ServerEnterWandering();
            }
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
                this.ServerSetVisualState(ZombieVisualState.Walk);
            }
        }

        [Server]
        protected virtual void OnServerDeath(uint killerNetId)
        {
            this.ServerPrepareForDeath(killerNetId);

            if (this.animator == null || this.deathController == null || this.deathDespawnDelay <= 0f)
            {
                NetworkServer.Destroy(this.gameObject);
                return;
            }

            this.ServerSetVisualState(ZombieVisualState.Death);
            this._deathDestroyCoroutine ??= this.StartCoroutine(this.ServerDestroyAfterDeathAnimation());
        }

        [Server]
        protected void ServerPrepareForDeath(uint killerNetId)
        {
            BattleManager.Instance?.ServerReportZombieKilled(this, killerNetId);
            this.ServerSetTargetable(false);
            this.StopAgent();
            if (this._agent != null)
            {
                this._agent.enabled = false;
            }
        }

        [Server]
        private IEnumerator ServerDestroyAfterDeathAnimation()
        {
            yield return new WaitForSeconds(this.deathDespawnDelay);
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
                this._agent.updateRotation = false;
            }
        }

        private bool IsAgentReady()
        {
            return this._agent != null && this._agent.enabled && this._agent.isOnNavMesh;
        }

        protected void StopAgent()
        {
            if (!this.IsAgentReady())
            {
                return;
            }

            this._agent.isStopped = true;
            this._agent.ResetPath();
        }

        [Server]
        private bool ServerFaceMovementTarget(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - this.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return true;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            this.transform.rotation = Quaternion.RotateTowards(
                this.transform.rotation,
                targetRotation,
                Mathf.Max(0f, this.turnSpeed) * Time.fixedDeltaTime);

            float angle = Quaternion.Angle(this.transform.rotation, targetRotation);
            bool aligned = angle <= Mathf.Max(0f, this.moveAlignmentAngle);
            if (!aligned && this.IsAgentReady())
            {
                this._agent.isStopped = true;
            }

            return aligned;
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
            this.ServerSetVisualState(ZombieVisualState.Idle);
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

        private void OnVisualStateSynced(ZombieVisualState oldValue, ZombieVisualState newValue)
        {
            this.ApplyVisualState(newValue);
        }

        [Server]
        protected void ServerSetVisualState(ZombieVisualState state)
        {
            if (this.visualState == state)
            {
                return;
            }

            this.visualState = state;
            this.ApplyVisualState(state);
        }

        protected float GetVisualStateAnimationDuration(ZombieVisualState state, float fallbackDuration)
        {
            RuntimeAnimatorController controller = this.GetVisualStateController(state);
            if (controller == null || controller.animationClips == null || controller.animationClips.Length == 0)
            {
                return Mathf.Max(0f, fallbackDuration);
            }

            float duration = 0f;
            foreach (AnimationClip clip in controller.animationClips)
            {
                if (clip != null)
                {
                    duration = Mathf.Max(duration, clip.length);
                }
            }

            return duration > 0f ? duration : Mathf.Max(0f, fallbackDuration);
        }

        private void ApplyVisualState(ZombieVisualState state)
        {
            if (this.animator == null)
            {
                return;
            }

            RuntimeAnimatorController controller = this.GetVisualStateController(state);

            if (controller == null || this.animator.runtimeAnimatorController == controller)
            {
                return;
            }

            this.animator.runtimeAnimatorController = controller;
            this.animator.Rebind();
            this.animator.Update(0f);
        }

        private RuntimeAnimatorController GetVisualStateController(ZombieVisualState state)
        {
            return state switch
            {
                ZombieVisualState.Spawn => this.spawnController,
                ZombieVisualState.Idle => this.idleController,
                ZombieVisualState.Walk => this.walkController,
                ZombieVisualState.Lunge => this.lungeController,
                ZombieVisualState.Death => this.deathController,
                ZombieVisualState.Explode => this.explodeController,
                _ => null,
            };
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
