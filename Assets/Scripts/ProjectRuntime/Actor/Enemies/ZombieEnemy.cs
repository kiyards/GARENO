using System.Collections;
using Mirror;
using ProjectRuntime.Combat;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectRuntime.Actor
{
    [DefaultExecutionOrder(-2)]
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
            Run,
            Lunge,
            Death,
            Explode,
        }

        [Header("Stats")]
        [SerializeField] private float moveSpeed = 2.4f;   // 0.8x survivor moveSpeed (3f)
        [SerializeField] private float wanderMoveSpeed;
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
        // While turning in place the zombie must get within this angle of its target heading
        // before it is allowed to start moving.
        [SerializeField] private float moveAlignmentAngle = 12f;
        // Once walking the zombie holds its heading and only stops to turn in place again if the
        // direction it needs to travel swings past this angle. Keeps it from pivoting mid-stride.
        [SerializeField] private float reorientAngle = 20f;

        [Header("Lunge")]
        [SerializeField] private float lungeDistance = 1.2f;
        [SerializeField] private float lungeDuration = 0.18f;

        [Header("Animation")]
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController visualController;
        [SerializeField] private string spawnStateName = "enemy_defaultzombie_spawn";
        [SerializeField] private string idleStateName = "enemy_defaultzombie_idle";
        [SerializeField] private string walkStateName = "enemy_defaultzombie_walk";
        [SerializeField] private string runStateName = "enemy_defaultzombie_walk";
        [SerializeField] private string lungeStateName = "enemy_defaultzombie_lunge";
        [SerializeField] private string deathStateName = "enemy_defaultzombie_death";
        [SerializeField] private string explodeStateName = "";
        [SerializeField] private float locomotionBlendDuration = 0.12f;
        [SerializeField] private float actionBlendDuration = 0.05f;
        [SerializeField] private float attackAnimationHoldDuration = 0.6f;
        [SerializeField] private float deathDespawnDelay = 3.5f;
        [SerializeField] private bool logVisualStateChanges;

        [Header("Audio")]
        [SerializeField] private float movementAudioSpeedThreshold = 0.05f;
        [SerializeField] private float movementAudioInterval = 0.55f;

        protected float AttackRange => this.attackRange;

        protected bool IsTargetable => this.isTargetable;

        [SyncVar(hook = nameof(OnTargetableSynced))]
        private bool isTargetable = true;

        [SyncVar(hook = nameof(OnVisualStateSynced))]
        private ZombieVisualState visualState = ZombieVisualState.Spawn;

        private bool hasAppliedVisualState;

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
        private bool _isReorienting;
        private Coroutine _deathDestroyCoroutine;
        private Vector3 _lastMovementAudioPosition;
        private bool _hasMovementAudioPosition;
        private float _nextMovementAudioTime;
        private NetworkAnimator _networkAnimator;

        private void Awake()
        {
            this.CacheComponents();
            this.ConfigureAgent();
            this.ApplyVisualState(this.visualState);
            this.EnsureNetworkAnimator();
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
            this._isReorienting = false;
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
            this.EnsureNetworkAnimator();
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

        private void Update()
        {
            this.TickLoopingVisualState();
            this.TickMovementAudio();
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
                    this.SetAgentSpeed(this.GetWanderMoveSpeed());
                    this.ServerSetVisualState(ZombieVisualState.Walk);
                    if (!this.ServerFaceMovementTarget(this._agent.steeringTarget))
                    {
                        // Still turning in place toward the new heading; keep the walk
                        // animation but don't translate yet (agent is stopped inside the call).
                        return;
                    }

                    this._agent.isStopped = false;
                    return;
                }

                this._hasWanderDestination = false;
                this._nextWanderMoveTime = NetworkTime.time + this.GetWanderPauseDuration(0f);
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
                this.SetAgentSpeed(this.GetWanderMoveSpeed());
                this._agent.SetDestination(point);
                this._hasWanderDestination = true;
                this.ServerSetVisualState(ZombieVisualState.Walk);
            }
            else
            {
                this._nextWanderMoveTime = NetworkTime.time + this.GetWanderPauseDuration(0.25f);
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

            this.SetAgentSpeed(this.moveSpeed);
            this._agent.SetDestination(this._target.transform.position);
            this.ServerSetVisualState(ZombieVisualState.Run);
            // Face where it is actually pathing (the steering target), not the player directly —
            // otherwise it faces the survivor while sliding sideways around obstacles.
            if (!this.ServerFaceMovementTarget(this._agent.steeringTarget))
            {
                // Still turning in place toward the new heading; don't translate yet.
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
            this.RpcPlayZombieAttackAudio(this.transform.position);

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
                this.ServerSetVisualState(ZombieVisualState.Run);
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
                this.ServerSetVisualState(ZombieVisualState.Run);
            }
        }

        [Server]
        protected virtual void OnServerDeath(uint killerNetId)
        {
            this.ServerPrepareForDeath(killerNetId);

            if (this.deathDespawnDelay <= 0f)
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

        private void EnsureNetworkAnimator()
        {
            this._networkAnimator ??= this.GetComponent<NetworkAnimator>();
            this._networkAnimator.animator = this.animator;
            this._networkAnimator.clientAuthority = false;
            this._networkAnimator.syncDirection = SyncDirection.ServerToClient;
        }

        private void ReinitializeNetworkAnimator()
        {
            bool wasEnabled = this._networkAnimator.enabled;
            this._networkAnimator.enabled = false;
            this._networkAnimator.enabled = wasEnabled;
        }

        private void ConfigureAgent()
        {
            if (this._agent != null)
            {
                this.SetAgentSpeed(this.moveSpeed);
                this._agent.updateRotation = false;
            }
        }

        private float GetWanderMoveSpeed()
        {
            return this.wanderMoveSpeed > 0f ? this.wanderMoveSpeed : this.moveSpeed;
        }

        private void SetAgentSpeed(float speed)
        {
            if (this._agent != null)
            {
                this._agent.speed = Mathf.Max(0f, speed);
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

        // Reports whether the zombie is facing its movement heading closely enough to translate this
        // tick. It rotates ONLY while turning in place (stopped) — never while moving — so it pivots
        // to face where it is going and then walks straight, instead of rotating as it moves.
        // A hysteresis band (moveAlignmentAngle..reorientAngle) keeps it from re-triggering a
        // turn-in-place on every minor course correction once it has committed to a heading.
        [Server]
        private bool ServerFaceMovementTarget(Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - this.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                // No meaningful heading (target is on top of us); nothing to turn toward.
                this._isReorienting = false;
                return true;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float angle = Quaternion.Angle(this.transform.rotation, targetRotation);

            if (!this._isReorienting)
            {
                // Committed to a heading and still roughly on it: walk straight, no rotation.
                if (angle <= Mathf.Max(0f, this.reorientAngle))
                {
                    return true;
                }

                // Heading swung too far to keep walking; stop and turn in place first.
                this._isReorienting = true;
            }

            if (this.IsAgentReady())
            {
                this._agent.isStopped = true;
            }

            this.transform.rotation = Quaternion.RotateTowards(
                this.transform.rotation,
                targetRotation,
                Mathf.Max(0f, this.turnSpeed) * Time.fixedDeltaTime);

            if (Quaternion.Angle(this.transform.rotation, targetRotation) <= Mathf.Max(0f, this.moveAlignmentAngle))
            {
                this._isReorienting = false;
                return true;
            }

            return false;
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
            this._nextWanderMoveTime = NetworkTime.time + this.GetWanderPauseDuration(0f);
            this.SetAgentSpeed(this.GetWanderMoveSpeed());
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

        protected bool IsValidTarget(GameplayPlayer player)
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
            if (!this.isServer && this._networkAnimator == null)
            {
                this.ApplyVisualState(newValue);
            }
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

        // Binds a runtime-instantiated model's Animator (for subclasses that build their visual at
        // runtime, e.g. the mimic copying a player model) and re-applies the current replicated
        // visual state so the newly bound animator starts on the right controller.
        protected void SetRuntimeAnimator(Animator runtimeAnimator)
        {
            this.animator = runtimeAnimator;
            this.EnsureNetworkAnimator();
            this.ReinitializeNetworkAnimator();
            this.ApplyVisualState(this.visualState);
        }

        // Re-scans for hit colliders and re-applies the current targetable gating. Needed when a
        // subclass adds colliders after Awake (e.g. a runtime-instantiated model), because
        // CacheComponents caches the collider set once and ApplyTargetable only gates what was
        // cached at spawn.
        protected void RefreshHitColliders()
        {
            this._hitColliders = null;
            this.CacheComponents();
            this.ApplyTargetable(this.isTargetable);
        }

        protected float GetVisualStateAnimationDuration(ZombieVisualState state, float fallbackDuration)
        {
            if (this.visualController == null || this.visualController.animationClips == null)
            {
                return Mathf.Max(0f, fallbackDuration);
            }

            string stateName = this.GetVisualStateName(state);
            foreach (AnimationClip clip in this.visualController.animationClips)
            {
                if (clip != null && clip.name == stateName)
                {
                    return clip.length;
                }
            }

            return Mathf.Max(0f, fallbackDuration);
        }

        protected virtual float GetWanderPauseDuration(float minimumDuration)
        {
            return Mathf.Max(minimumDuration, this.wanderPauseDuration);
        }

        private void ApplyVisualState(ZombieVisualState state)
        {
            if (this.animator == null)
            {
                this.LogVisualStateIssue(state, "no Animator assigned");
                return;
            }

            string stateName = this.GetVisualStateName(state);

            if (string.IsNullOrEmpty(stateName))
            {
                this.LogVisualStateIssue(state, "no Animator state assigned");
                return;
            }

            bool controllerChanged =
                this.animator.runtimeAnimatorController != this.visualController;
            if (controllerChanged)
            {
                this.animator.speed = 1f;
                this.animator.runtimeAnimatorController = this.visualController;
                this.animator.Rebind();
            }

            this.animator.speed = 1f;
            bool isFirstApply = !this.hasAppliedVisualState;
            this.hasAppliedVisualState = true;
            if (isFirstApply || controllerChanged)
            {
                // Nothing meaningful to blend from on the first pose or right after the
                // controller is (re)bound (e.g. a runtime-built model) — snap instantly.
                this.animator.Play(stateName, 0, 0f);
                this.animator.Update(0f);
            }
            else
            {
                this.animator.CrossFadeInFixedTime(
                    stateName,
                    this.GetVisualStateBlendDuration(state),
                    0,
                    0f
                );
            }

            this.LogVisualStateApplied(state, stateName);
        }

        private float GetVisualStateBlendDuration(ZombieVisualState state)
        {
            switch (state)
            {
                case ZombieVisualState.Idle:
                case ZombieVisualState.Walk:
                case ZombieVisualState.Run:
                    return this.locomotionBlendDuration;
                default:
                    // Spawn / Lunge / Death / Explode — keep these snappy.
                    return this.actionBlendDuration;
            }
        }

        private void TickLoopingVisualState()
        {
            if (this.animator == null ||
                !this.animator.enabled ||
                this.animator.layerCount <= 0 ||
                !this.IsLoopingVisualState(this.visualState))
            {
                return;
            }

            AnimatorStateInfo stateInfo = this.animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.loop || stateInfo.normalizedTime < 0.98f)
            {
                return;
            }

            this.animator.Play(this.GetVisualStateName(this.visualState), 0, 0f);
            this.animator.Update(0f);
        }

        private bool IsLoopingVisualState(ZombieVisualState state)
        {
            return state == ZombieVisualState.Spawn ||
                   state == ZombieVisualState.Idle ||
                   state == ZombieVisualState.Walk ||
                   state == ZombieVisualState.Run;
        }

        private void TickMovementAudio()
        {
            if (
                !NetworkClient.active
                || (this.visualState != ZombieVisualState.Walk && this.visualState != ZombieVisualState.Run)
                || Time.deltaTime <= 0f
            )
            {
                this.ResetMovementAudioTracking();
                return;
            }

            Vector3 currentPosition = transform.position;
            if (!this._hasMovementAudioPosition)
            {
                this._lastMovementAudioPosition = currentPosition;
                this._hasMovementAudioPosition = true;
                return;
            }

            Vector3 delta = currentPosition - this._lastMovementAudioPosition;
            this._lastMovementAudioPosition = currentPosition;
            delta.y = 0f;

            if (
                delta.magnitude / Time.deltaTime < this.movementAudioSpeedThreshold
                || Time.time < this._nextMovementAudioTime
            )
            {
                return;
            }

            AudioManager.Instance?.PlayOneShot(AudioEventIds.ZombieWalkSfx, currentPosition);
            this._nextMovementAudioTime = Time.time + this.movementAudioInterval;
        }

        private void ResetMovementAudioTracking()
        {
            this._hasMovementAudioPosition = false;
            this._nextMovementAudioTime = Time.time;
        }

        [ClientRpc]
        private void RpcPlayZombieAttackAudio(Vector3 worldPos)
        {
            AudioManager.Instance?.PlayOneShot(AudioEventIds.ZombieAttackSfx, worldPos);
        }

        private void LogVisualStateIssue(ZombieVisualState state, string reason)
        {
            if (!this.logVisualStateChanges)
            {
                return;
            }

            Debug.LogWarning(
                $"[{this.name}] Visual state {state} not applied: {reason}.",
                this);
        }

        private void LogVisualStateApplied(ZombieVisualState state, string stateName)
        {
            if (!this.logVisualStateChanges)
            {
                return;
            }

            Debug.Log(
                $"[{this.name}] Visual state {state} applied. " +
                $"controller={this.visualController.name} stateName={stateName} " +
                $"controllerClips={this.FormatControllerClips(this.visualController)} " +
                $"animator={this.animator.name} animatorEnabled={this.animator.enabled} " +
                $"animatorActive={this.animator.gameObject.activeInHierarchy} " +
                $"currentClips={this.FormatCurrentAnimatorClips()}",
                this);
        }

        private string FormatControllerClips(RuntimeAnimatorController controller)
        {
            AnimationClip[] clips = controller.animationClips;
            if (clips == null || clips.Length == 0)
            {
                return "<none>";
            }

            string value = string.Empty;
            for (int i = 0; i < clips.Length; i++)
            {
                AnimationClip clip = clips[i];
                if (i > 0)
                {
                    value += ", ";
                }

                value += clip != null
                    ? $"{clip.name}({clip.length:0.###}s)"
                    : "<null>";
            }

            return value;
        }

        private string FormatCurrentAnimatorClips()
        {
            if (this.animator.layerCount <= 0)
            {
                return "<no layers>";
            }

            AnimatorClipInfo[] clips = this.animator.GetCurrentAnimatorClipInfo(0);
            if (clips == null || clips.Length == 0)
            {
                return "<none>";
            }

            string value = string.Empty;
            for (int i = 0; i < clips.Length; i++)
            {
                if (i > 0)
                {
                    value += ", ";
                }

                AnimationClip clip = clips[i].clip;
                value += clip != null
                    ? $"{clip.name}(weight={clips[i].weight:0.###})"
                    : "<null>";
            }

            return value;
        }

        private string GetVisualStateName(ZombieVisualState state)
        {
            return state switch
            {
                ZombieVisualState.Spawn => this.spawnStateName,
                ZombieVisualState.Idle => this.idleStateName,
                ZombieVisualState.Walk => this.walkStateName,
                ZombieVisualState.Run => this.runStateName,
                ZombieVisualState.Lunge => this.lungeStateName,
                ZombieVisualState.Death => this.deathStateName,
                ZombieVisualState.Explode => this.explodeStateName,
                _ => string.Empty,
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
