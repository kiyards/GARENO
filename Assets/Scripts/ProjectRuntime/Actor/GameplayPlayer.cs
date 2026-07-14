using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using ProjectRuntime.UI;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectRuntime.Actor
{
    public enum CharacterMode
    {
        SHOULDER,
        AIM,
        SPECTATE,
        TOP_DOWN,
    }

    public enum DownedResolution
    {
        Revived,
        TimedOut,
    }

    public class GameplayPlayer : NetworkStateMachine
    {
        [Header("Components")]
        [field: SerializeField]
        public PlayerManager localManager { get; private set; }

        [field: SerializeField]
        public PlayerInput input { get; private set; }

        [field: SerializeField]
        public CameraController cam { get; private set; }

        [field: SerializeField]
        public SphereGroundCheck groundCheck { get; private set; }

        [field: SerializeField]
        public Rigidbody rb { get; private set; }

        [field: SerializeField]
        public Collider col { get; private set; }

        [field: SerializeField]
        public Health health { get; private set; }

        [Header("Anchors")]
        [field: SerializeField]
        public Transform aimRig { get; private set; }

        [Header("Visuals")]
        [SerializeField]
        private GameObject corpseVisualPrefab;

        [SerializeField]
        private float corpseVisualYawOffset = 180f;

        [Header("Stats")]
        public float jumpForce = 2.5f;
        public float moveSpeed = 3f;

        [Header("Dungeon Master")]
        [SerializeField, FormerlySerializedAs("mastermindHorizontalSpeed")]
        private float dungeonMasterHorizontalSpeed = 18f;

        [SerializeField, FormerlySerializedAs("mastermindVerticalSpeed")]
        private float dungeonMasterVerticalSpeed = 12f;

        [SerializeField]
        private float dungeonMasterMinY = 0f;

        [SerializeField]
        private float dungeonMasterMaxY = 40f;

        [Header("Effects")]
        [SerializeField]
        private float survivorFootstepSpeedThreshold = 0.15f;

        [SerializeField]
        private float survivorFootstepInterval = 0.45f;

        private FlashEffect _flashEffect;

        public void SetFlashEffect(FlashEffect effect)
        {
            _flashEffect = effect;
        }

        [Header("Revive")]
        // How long a downed survivor waits before auto-resolving if no teammate revives them.
        [SerializeField]
        private float reviveWindow = 30f;

        // How long a teammate must hold Interact in range to revive a downed survivor.
        [SerializeField]
        private float reviveHoldTime = 2f;

        // How close a teammate must be to revive a downed survivor.
        [SerializeField]
        private float reviveRange = 2.5f;

        // If no revive contact arrives for this long, the hold streak is considered broken (the
        // reviver left range or stopped holding Interact) and the next contact starts a fresh hold.
        private const float ReviveContactGrace = 0.25f;

        public float ReviveHoldTime => reviveHoldTime;

        // Client-facing countdown for the local downed survivor's own HUD, backed by DownedState's
        // replicated totalDuration/elapsedTime rather than the server-only _downedStartTime below.
        public float DownedTimeRemaining =>
            currentState is DownedState downedState
                ? Mathf.Max(0f, downedState.totalDuration - downedState.elapsedTime)
                : 0f;

        private double _downedStartTime;
        private double _downedPresentationEndTime;

        // Server time (NetworkTime.time) when the current continuous revive-hold streak began, and
        // the last time a valid revive contact arrived. Hold duration is measured in real time so it
        // can't be outrun by command batching/frame-rate.
        private double _reviveContactStartTime;
        private double _lastReviveContactTime;
        private uint _downedSourceNetId;

        // Guards against re-resolving while the downed→respawn state transition round-trips back from
        // the owning client (the state authority), which would otherwise re-fire every physics frame.
        private bool _downedResolved;
        private bool _downedReported;
        private Coroutine _downedPresentationCoroutine;

        private bool _isGhost;
        private float _speedMultiplier = 1f;
        private float _slowMultiplier = 1f;
        private float _speedBoostMultiplier = 1f;
        private int _slowStackCount;
        private Coroutine _speedBoostCoroutine;

        private PlayerRole _currentRole = PlayerRole.Unassigned;
        private DungeonMasterCardManager _cardManager;
        public DungeonMasterCardManager CardManager =>
            this._cardManager ??= this.GetComponent<DungeonMasterCardManager>();
        private DungeonMasterTurretController _turret;
        public DungeonMasterTurretController Turret =>
            this._turret != null ? this._turret : this._turret = this.EnsureTurretController();
        private DungeonMasterTrapController _trapController;
        public DungeonMasterTrapController TrapController =>
            this._trapController != null
                ? this._trapController
                : this._trapController = this.EnsureTrapController();
        private DungeonMasterNemesisController _nemesis;
        public DungeonMasterNemesisController Nemesis =>
            this._nemesis != null ? this._nemesis : this._nemesis = this.EnsureNemesisController();
        private SurvivorAbilityController _survivorAbilities;
        public SurvivorAbilityController SurvivorAbilities =>
            this._survivorAbilities != null
                ? this._survivorAbilities
                : this._survivorAbilities = GetComponent<SurvivorAbilityController>();
        private Renderer[] _roleRenderers;
        private bool[] _roleRendererInitialEnabled;
        private bool _initialColliderEnabled;
        private bool _initialRigidbodyUseGravity;
        private bool _initialRigidbodyIsKinematic;
        private bool _cachedRoleDefaults;
        private Coroutine _deadBodyTransitionCoroutine;
        private Vector3 _lastFootstepPosition;
        private bool _hasFootstepPosition;
        private float _nextFootstepTime;

        public float SpeedMultiplier => _speedMultiplier;
        public bool IsInactive => currentState is BaseInactiveState;
        public bool IsBearTrapped => currentState is BearTrappedState;
        public bool IsDowned => currentState is DownedState;
        public bool IsDungeonMaster => _currentRole == PlayerRole.DungeonMaster;
        private bool CanReviveTeammates =>
            localManager.playerRole == PlayerRole.Survivor && !IsInactive && health.IsAlive;
        public float DungeonMasterHorizontalSpeed => dungeonMasterHorizontalSpeed;
        public float DungeonMasterVerticalSpeed => dungeonMasterVerticalSpeed;
        public override NetworkBaseState StartState =>
            IsDungeonMaster ? new DungeonMasterMovementState(this) : new BaseMovementState(this);
        public override NetworkBaseState DefaultState =>
            IsDungeonMaster ? new DungeonMasterMovementState(this) : new BaseMovementState(this);

        protected override void Awake()
        {
            base.Awake();
            CacheRoleDefaults();
            Turret.SetVisible(false);
            TrapController.Initialize(this);
            EnsureDoorInteractor();
        }

        public override void NetworkStart()
        {
            base.NetworkStart();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (health != null)
                health.OnDeathEvent += OnHealthDepleted;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (health != null)
                health.OnDeathEvent -= OnHealthDepleted;
        }

        public void PlayAcceptedJumpVisual()
        {
            GetComponent<PlayerVisualAnimator>().PlayJump();
        }

        [Server]
        private void OnHealthDepleted(uint killerNetId)
        {
            ServerEnterDowned(killerNetId);
        }

        protected override void Update()
        {
            base.Update();
            this.TickSurvivorFootstepAudio();

            if (isLocalPlayer)
            {
                ClientTickDungeonMasterJumpHotkeys();
            }
        }

        private void TickSurvivorFootstepAudio()
        {
            if (
                !NetworkClient.active
                || IsDungeonMaster
                || IsInactive
                || Time.deltaTime <= 0f
            )
            {
                this.ResetFootstepAudioTracking();
                return;
            }

            Vector3 currentPosition = transform.position;
            if (!this._hasFootstepPosition)
            {
                this._lastFootstepPosition = currentPosition;
                this._hasFootstepPosition = true;
                return;
            }

            Vector3 delta = currentPosition - this._lastFootstepPosition;
            this._lastFootstepPosition = currentPosition;
            delta.y = 0f;

            if (
                !groundCheck.IsGrounded
                || delta.magnitude / Time.deltaTime < this.survivorFootstepSpeedThreshold
                || Time.time < this._nextFootstepTime
            )
            {
                return;
            }

            AudioManager.Instance?.PlayOneShot(AudioEventIds.PlayerFootstepSfx, currentPosition);
            this._nextFootstepTime = Time.time + this.survivorFootstepInterval;
        }

        private void ResetFootstepAudioTracking()
        {
            this._hasFootstepPosition = false;
            this._nextFootstepTime = Time.time;
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();

            if (isServer)
            {
                ServerTickDowned();

                if (!IsDungeonMaster && !IsInactive && transform.position.y < -10f)
                {
                    ServerEnterDowned(0);
                }
            }

            if (isLocalPlayer)
            {
                ClientTickRevive();
            }
        }

        public void ApplyRole(PlayerRole role)
        {
            CacheRoleDefaults();
            _currentRole = role;

            if (role == PlayerRole.DungeonMaster)
            {
                ApplyDungeonMasterBody();
                QueueRoleState(new DungeonMasterMovementState(this));
                return;
            }

            Turret.SetVisible(false);
            ApplySurvivorBody();
            QueueRoleState(new BaseMovementState(this));
        }

        public Vector3 ClampDungeonMasterPosition(Vector3 position)
        {
            var minY = Mathf.Min(dungeonMasterMinY, dungeonMasterMaxY);
            var maxY = Mathf.Max(dungeonMasterMinY, dungeonMasterMaxY);
            position.y = Mathf.Clamp(position.y, minY, maxY);
            return position;
        }

        // Sets up the ghost body: a permanently-dead survivor still walks and collides with the world
        // (normal survivor physics), but passes through every other player. The normal model is swapped
        // to the ghost visual; per-viewer visibility is handled by RefreshGhostVisibility.
        private void EnterGhostBody()
        {
            CacheRoleDefaults();
            _isGhost = true;
            GetComponent<PlayerVisualAnimator>().EnterGhostMode();

            if (col != null)
            {
                col.enabled = _initialColliderEnabled;
            }

            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = _initialRigidbodyIsKinematic;
                rb.useGravity = _initialRigidbodyUseGravity;
            }

            SetLayerRecursive(gameObject, LayerMask.NameToLayer("Ghost"));
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void SpawnCorpse(Vector3 position, Quaternion rotation)
        {
            var corpseRotation = rotation * Quaternion.Euler(0f, corpseVisualYawOffset, 0f);
            var corpse = Instantiate(corpseVisualPrefab, position, corpseRotation);
            corpse.name = "Corpse";

            foreach (var corpseCollider in corpse.GetComponentsInChildren<Collider>())
            {
                Destroy(corpseCollider);
            }

            GetComponent<PlayerVisualAnimator>()
                .ApplyDeathPose(corpse.GetComponentInChildren<Animator>());
        }

        private void BeginDeadBodyTransition(Vector3 position, Quaternion rotation, float delay)
        {
            if (_deadBodyTransitionCoroutine != null)
            {
                StopCoroutine(_deadBodyTransitionCoroutine);
            }

            _deadBodyTransitionCoroutine = StartCoroutine(
                ApplyDeadBodyAfterDelay(position, rotation, delay)
            );
        }

        private IEnumerator ApplyDeadBodyAfterDelay(
            Vector3 position,
            Quaternion rotation,
            float delay
        )
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            SpawnCorpse(position, rotation);
            EnterGhostBody();
            RefreshGhostVisibility();

            if (isLocalPlayer)
            {
                GameplayPlayer.RefreshAllGhostVisibility();
            }
        }

        // A ghost (permanently dead survivor) is visible only to the Dungeon Master and to other dead
        // survivors — never to living survivors. Each client decides locally from its own viewer.
        private void RefreshGhostVisibility()
        {
            if (!_isGhost)
            {
                return;
            }

            bool canSee = LocalViewerCanSeeGhosts();
            GetComponent<PlayerVisualAnimator>().SetGhostVisible(canSee);
        }

        private static bool LocalViewerCanSeeGhosts()
        {
            var local = PlayerManager.Instance;
            if (local == null)
            {
                return false;
            }

            if (local.playerRole == PlayerRole.DungeonMaster)
            {
                return true;
            }

            // Use _isGhost rather than IsDead: when the local player has only just died, this runs from
            // inside DeadState.OnEnter before the state machine has assigned currentState, so IsDead would
            // still read false. _isGhost is already set by EnterGhostBody at that point.
            return local.player != null && local.player._isGhost;
        }

        // Re-evaluate every ghost's visibility on this client. Call when the local viewer's eligibility
        // changes — e.g. the local survivor just died and can now see ghosts.
        private static void RefreshAllGhostVisibility()
        {
            var battleManager = BattleManager.Instance;
            if (battleManager == null)
            {
                return;
            }

            foreach (var pm in battleManager.Players)
            {
                if (pm != null && pm.player != null)
                {
                    pm.player.RefreshGhostVisibility();
                }
            }
        }

        [Server]
        public void ServerEnterDowned(uint sourceNetId = 0)
        {
            if (IsDungeonMaster || IsInactive)
            {
                return;
            }

            var downedPresentationDelay = GetComponent<PlayerVisualAnimator>()
                .GetDeathAnimationDuration(0f);
            _downedStartTime = NetworkTime.time + downedPresentationDelay;
            _downedPresentationEndTime = _downedStartTime;
            _downedSourceNetId = sourceNetId;
            // Sentinel in the past so the first contact always starts a fresh hold streak.
            _lastReviveContactTime = double.NegativeInfinity;
            _reviveContactStartTime = 0d;
            _downedResolved = false;
            _downedReported = false;

            ServerForceState(
                new DownedState(this)
                {
                    m_anchorPosition = transform.position,
                    totalDuration = downedPresentationDelay + reviveWindow,
                }
            );

            if (_downedPresentationCoroutine != null)
            {
                StopCoroutine(_downedPresentationCoroutine);
            }

            _downedPresentationCoroutine = StartCoroutine(
                ServerCompleteDownedPresentation(sourceNetId, downedPresentationDelay)
            );
        }

        private IEnumerator ServerCompleteDownedPresentation(uint sourceNetId, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (!IsDowned || _downedResolved)
            {
                yield break;
            }

            ServerReportDownedIfNeeded(sourceNetId);
            BattleManager.Instance?.ServerRefreshSurvivorDefeatState();
        }

        [Server]
        private void ServerReportDownedIfNeeded(uint sourceNetId)
        {
            if (_downedReported)
            {
                return;
            }

            _downedReported = true;
            BattleManager.Instance?.ServerReportSurvivorDowned(localManager, sourceNetId);
        }

        [Server]
        private void ServerTickDowned()
        {
            if (!IsDowned)
            {
                return;
            }

            if (NetworkTime.time < _downedPresentationEndTime)
            {
                return;
            }

            if (
                BattleManager.Instance != null
                && BattleManager.Instance.CurrentRoundPhase == RoundPhase.RoundComplete
            )
            {
                return;
            }

            if (NetworkTime.time - _downedStartTime >= reviveWindow)
            {
                ServerResolveDowned(DownedResolution.TimedOut);
            }
        }

        // Called on the reviving survivor; downedNetId identifies the teammate being revived. Sent
        // each FixedUpdate while the reviver holds Interact in range, mirroring CmdMashBearTrap.
        [Command]
        public void CmdReviveTeammate(uint downedNetId)
        {
            if (!CanReviveTeammates)
            {
                return;
            }

            if (!NetworkServer.spawned.TryGetValue(downedNetId, out NetworkIdentity identity))
            {
                return;
            }

            // The NetworkIdentity is on the player root, but GameplayPlayer lives on a child object,
            // so GetComponent on the identity's GameObject misses it — search children.
            var target = identity.GetComponentInChildren<GameplayPlayer>();
            if (target == null || target == this || !target.IsDowned)
            {
                return;
            }

            if (
                (target.transform.position - transform.position).sqrMagnitude
                > reviveRange * reviveRange
            )
            {
                return;
            }

            target.ServerRegisterReviveContact(this);
        }

        // Called on the downed survivor each time a teammate channels a valid revive. The hold is
        // measured as continuous real-time contact: a gap longer than the grace restarts the streak,
        // and reviveHoldTime seconds of unbroken contact completes the revive.
        [Server]
        public void ServerRegisterReviveContact(GameplayPlayer reviver)
        {
            if (!IsDowned || reviver == null || reviver == this || !reviver.CanReviveTeammates)
            {
                return;
            }

            if (NetworkTime.time < _downedPresentationEndTime)
            {
                return;
            }

            double now = NetworkTime.time;
            if (now - _lastReviveContactTime > ReviveContactGrace)
            {
                _reviveContactStartTime = now;
            }

            _lastReviveContactTime = now;
            if (now - _reviveContactStartTime >= reviveHoldTime)
            {
                ServerResolveDowned(DownedResolution.Revived);
            }
        }

        // Single resolution point for both revive-completed and revive-window-expired.
        [Server]
        public bool ServerCanResolveDowned()
        {
            return IsDowned && NetworkTime.time >= _downedPresentationEndTime;
        }

        [Server]
        public void ServerResolveDowned(DownedResolution resolution)
        {
            if (!IsDowned || _downedResolved)
            {
                return;
            }

            if (!ServerCanResolveDowned())
            {
                return;
            }

            _downedResolved = true;
            ServerReportDownedIfNeeded(_downedSourceNetId);

            if (resolution == DownedResolution.Revived)
            {
                BattleManager.Instance?.ServerReportSurvivorRevived(localManager, _downedSourceNetId);
                if (health != null)
                {
                    health.ServerResetHealth();
                }

                Vector3 respawnPos = GameNetworkManager.Instance.GetGameplaySpawnPosition(localManager);
                RpcEnterRespawnState(respawnPos);
            }
            else
            {
                BattleManager.Instance?.ServerReportSurvivorTimedOut(localManager, _downedSourceNetId);
                if (health != null)
                {
                    health.ServerResetHealth();
                }

                Vector3 respawnPos = GameNetworkManager.Instance.GetGameplaySpawnPosition(localManager);
                RpcEnterRespawnState(respawnPos);
            }

            // A death may have emptied the survivor pool — re-check the DM win condition.
            BattleManager.Instance?.ServerRefreshSurvivorDefeatState();
        }

        private void ClientTickRevive()
        {
            if (!CanReviveTeammates || input == null || !input.InteractHold)
            {
                return;
            }

            var target = FindReviveTarget();
            if (target != null)
            {
                CmdReviveTeammate(target.netId);
            }
        }

        private void ClientTickDungeonMasterJumpHotkeys()
        {
            if (
                !IsDungeonMaster
                || input == null
                || !(currentState is DungeonMasterMovementState)
                || !input.TryGetDungeonMasterJumpSlot(out int slotIndex)
                || !TryGetDungeonMasterJumpPosition(slotIndex, out Vector3 jumpPosition)
            )
            {
                return;
            }

            JumpDungeonMasterTo(jumpPosition);
        }

        private bool TryGetDungeonMasterJumpPosition(int slotIndex, out Vector3 jumpPosition)
        {
            jumpPosition = Vector3.zero;
            if (slotIndex < 0)
            {
                return false;
            }

            var battleManager = BattleManager.Instance;
            if (battleManager == null)
            {
                return false;
            }

            var survivors = new List<PlayerManager>();
            foreach (var playerManager in battleManager.Players)
            {
                if (
                    playerManager != null
                    && playerManager.playerRole == PlayerRole.Survivor
                    && playerManager.player != null
                )
                {
                    survivors.Add(playerManager);
                }
            }

            survivors.Sort(ComparePlayerManagers);
            if (slotIndex >= survivors.Count)
            {
                return false;
            }

            jumpPosition = GetDungeonMasterJumpTargetPosition(survivors[slotIndex].player);
            return true;
        }

        private void JumpDungeonMasterTo(Vector3 targetPosition)
        {
            targetPosition.y = transform.position.y;
            Vector3 clampedPosition = ClampDungeonMasterPosition(targetPosition);

            if (rb != null)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.position = clampedPosition;
            }

            transform.position = clampedPosition;
        }

        private static Vector3 GetDungeonMasterJumpTargetPosition(GameplayPlayer target)
        {
            return target.transform.position;
        }

        private static int ComparePlayerManagers(PlayerManager a, PlayerManager b)
        {
            int indexCompare = a.playerIndex.CompareTo(b.playerIndex);
            if (indexCompare != 0)
            {
                return indexCompare;
            }

            return a.netId.CompareTo(b.netId);
        }

        // Public so client-side UI (e.g. a revive prompt) can query the same target this survivor
        // would revive, without duplicating the range/eligibility logic.
        public GameplayPlayer FindReviveTarget()
        {
            if (!CanReviveTeammates)
            {
                return null;
            }

            var battleManager = BattleManager.Instance;
            if (battleManager == null)
            {
                return null;
            }

            GameplayPlayer nearest = null;
            float nearestSqr = reviveRange * reviveRange;
            foreach (var pm in battleManager.Players)
            {
                var candidate = pm != null ? pm.player : null;
                if (candidate == null || candidate == this || !candidate.IsDowned)
                {
                    continue;
                }

                float sqr = (candidate.transform.position - transform.position).sqrMagnitude;
                if (sqr <= nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = candidate;
                }
            }

            return nearest;
        }

        [Command]
        public void CmdFireDungeonMasterTurret(
            uint targetNetId,
            Vector3 hitPoint,
            Vector3 fireDirection,
            Vector3 hitNormal,
            int hitLayer
        )
        {
            Turret.ServerFire(targetNetId, hitPoint, fireDirection, hitNormal, hitLayer);
        }

        [Command]
        public void CmdUpdateDungeonMasterTurretAim(Vector3 worldDirection)
        {
            Turret.ServerUpdateAim(worldDirection);
        }

        [Command]
        public void CmdBeginTurretDisassemble()
        {
            Turret.ServerBeginDisassemble();
        }

        [Command]
        public void CmdStartTurretLifetime()
        {
            Turret.ServerStartTurretLifetime();
        }

        // Sent when the Dungeon Master confirms Nemesis placement (green indicator + charge). The card
        // manager validates availability and spawns the Nemesis at the chosen ground position.
        [Command]
        public void CmdActivateNemesisAt(Vector3 groundPosition)
        {
            CardManager.ServerTryActivateNemesis(groundPosition);
        }

        // Ends the Nemesis before its lifetime expires, returning the DM to top-down placement.
        [Command]
        public void CmdEndNemesisEarly()
        {
            Nemesis.ServerBeginDisassemble();
        }

        [Command]
        public void CmdNemesisAttack(int attackType)
        {
            Nemesis.ServerExecuteAttack((NemesisAttackType)attackType);
        }

        [Command]
        public void CmdTryLockDoor(uint doorNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(doorNetId, out NetworkIdentity doorIdentity))
            {
                return;
            }

            var door =
                doorIdentity.GetComponent<LockableDoor>()
                ?? doorIdentity.GetComponentInChildren<LockableDoor>();
            door?.ServerTryLock(localManager);
        }

        [Command]
        public void CmdPlaceTrap(TrapType trapType, Vector3 position, Vector3 normal)
        {
            TrapController.ServerPlace(trapType, position, normal);
        }

        [Command]
        public void CmdMashBearTrap(uint trapNetId)
        {
            if (!NetworkServer.spawned.TryGetValue(trapNetId, out NetworkIdentity trapIdentity))
            {
                return;
            }

            var bearTrap = trapIdentity.GetComponent<BearTrap>();
            if (bearTrap == null)
            {
                return;
            }

            bearTrap.ServerHandleMash(this);
        }

        [Command]
        public void CmdActivateHealCircle()
        {
            SurvivorAbilities?.ServerTryActivateHealCircle();
        }

        [Command]
        public void CmdActivateMolotov(Vector3 aimDirection)
        {
            SurvivorAbilities?.ServerTryActivateMolotov(aimDirection);
        }

        [Command]
        public void CmdActivateSteroid()
        {
            SurvivorAbilities?.ServerTryActivateSteroid();
        }

        [Command]
        public void CmdActivateEmp()
        {
            SurvivorAbilities?.ServerTryActivateEmp();
        }

        [Server]
        public void ServerApplySlow(float slowAmount, float duration)
        {
            _slowStackCount++;
            _slowMultiplier = Mathf.Clamp(1f - _slowStackCount * slowAmount, 0f, 1f);
            RefreshSpeedMultiplier();
            StartCoroutine(ExpireSlowStack(slowAmount, duration));
        }

        [TargetRpc]
        private void TargetSetSpeedMultiplier(NetworkConnectionToClient conn, float multiplier)
        {
            _speedMultiplier = multiplier;
        }

        private IEnumerator ExpireSlowStack(float slowAmount, float duration)
        {
            yield return new WaitForSeconds(duration);
            _slowStackCount = Mathf.Max(0, _slowStackCount - 1);
            _slowMultiplier = Mathf.Clamp(1f - _slowStackCount * slowAmount, 0f, 1f);
            RefreshSpeedMultiplier();
        }

        [Server]
        public void ServerImmobilize(float duration)
        {
            if (IsDungeonMaster || IsInactive)
                return;
            ServerApplySlow(1f, duration);
        }

        [Server]
        public void ServerApplySpeedBoost(float multiplier, float duration)
        {
            if (IsDungeonMaster || IsInactive)
            {
                return;
            }

            if (_speedBoostCoroutine != null)
            {
                StopCoroutine(_speedBoostCoroutine);
            }

            _speedBoostMultiplier = Mathf.Max(1f, multiplier);
            RefreshSpeedMultiplier();
            _speedBoostCoroutine = StartCoroutine(ExpireSpeedBoost(duration));
        }

        [Server]
        public void ServerApplyKnockback(Vector3 impulse)
        {
            if (IsDungeonMaster || IsInactive || IsDowned || connectionToClient == null)
                return;
            TargetApplyKnockback(connectionToClient, impulse);
        }

        [TargetRpc]
        private void TargetApplyKnockback(NetworkConnectionToClient conn, Vector3 impulse)
        {
            if (rb == null || rb.isKinematic)
                return;
            rb.AddForce(impulse, ForceMode.VelocityChange);
        }

        [Server]
        public void ServerApplyFlash(Vector3 flashPosition, float maxDuration)
        {
            if (IsDungeonMaster || IsInactive || IsDowned || connectionToClient == null)
                return;
            TargetApplyFlash(connectionToClient, flashPosition, maxDuration);
        }

        [TargetRpc]
        private void TargetApplyFlash(
            NetworkConnectionToClient conn,
            Vector3 flashPosition,
            float maxDuration
        )
        {
            if (_flashEffect == null)
                return;
            // Camera.main is valid here — TargetRpc only runs on the owning client.
            Vector3 eyePos =
                Camera.main != null
                    ? Camera.main.transform.position
                    : transform.position + Vector3.up * 1.6f;
            Vector3 camFwd =
                Camera.main != null ? Camera.main.transform.forward : transform.forward;
            Vector3 dirToFlash = (flashPosition - eyePos).normalized;
            // dot=1: looking directly at flash (full blind). dot<=0: looking away (no effect).
            float dot = Vector3.Dot(camFwd, dirToFlash);
            float intensity = Mathf.Clamp01(Mathf.InverseLerp(0f, 1f, dot));
            float duration = maxDuration * intensity;
            if (duration > 0.05f)
                _flashEffect.StartFlash(duration, intensity);
        }

        [Server]
        public void ServerEnterBearTrap(BearTrap trap, Vector3 anchorPosition)
        {
            if (trap == null || IsDungeonMaster || IsInactive)
            {
                return;
            }

            ServerForceState(
                new BearTrappedState(this)
                {
                    m_trapNetId = trap.netId,
                    m_anchorPosition = anchorPosition,
                }
            );
        }

        [Server]
        public void ServerExitBearTrap(uint trapNetId)
        {
            if (
                !(currentState is BearTrappedState trappedState)
                || trappedState.m_trapNetId != trapNetId
            )
            {
                return;
            }

            ServerForceState(new BaseMovementState(this));
        }

        [ClientRpc]
        public void RpcEnterRespawnState(Vector3 respawnPos)
        {
            if (IsDungeonMaster)
            {
                return;
            }

            var respawnState = new RespawnState(this) { m_respawnPos = respawnPos };
            QueueState(respawnState);
        }

        private void QueueRoleState(NetworkBaseState roleState)
        {
            if (!authority)
            {
                return;
            }

            if (currentState == null)
            {
                return;
            }

            if (currentState.GetType() == roleState.GetType())
            {
                return;
            }

            if (nextState != null && nextState.GetType() == roleState.GetType())
            {
                return;
            }

            QueueState(roleState);
        }

        private void CacheRoleDefaults()
        {
            if (_cachedRoleDefaults)
            {
                return;
            }

            _roleRenderers = GetComponentsInChildren<Renderer>(true);
            _roleRendererInitialEnabled = new bool[_roleRenderers.Length];
            for (var i = 0; i < _roleRenderers.Length; i++)
            {
                _roleRendererInitialEnabled[i] =
                    _roleRenderers[i] != null && _roleRenderers[i].enabled;
            }

            _initialColliderEnabled = col == null || col.enabled;
            _initialRigidbodyUseGravity = rb == null || rb.useGravity;
            _initialRigidbodyIsKinematic = rb != null && rb.isKinematic;
            _cachedRoleDefaults = true;
        }

        private void ApplyDungeonMasterBody()
        {
            SetRenderersVisible(false);
            Turret.SetVisible(false);

            if (col != null)
            {
                col.enabled = false;
            }

            if (rb == null)
            {
                return;
            }

            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.useGravity = false;
            rb.isKinematic = true;

            var clampedPosition = ClampDungeonMasterPosition(rb.position);
            rb.position = clampedPosition;
            transform.position = clampedPosition;
        }

        private void ApplySurvivorBody()
        {
            SetRenderersVisible(true);

            if (col != null)
            {
                col.enabled = _initialColliderEnabled;
            }

            if (rb == null)
            {
                return;
            }

            rb.isKinematic = _initialRigidbodyIsKinematic;
            rb.useGravity = _initialRigidbodyUseGravity;
        }

        private void SetRenderersVisible(bool isVisible)
        {
            if (_roleRenderers == null || _roleRendererInitialEnabled == null)
            {
                return;
            }

            for (var i = 0; i < _roleRenderers.Length; i++)
            {
                if (_roleRenderers[i] == null)
                {
                    continue;
                }

                _roleRenderers[i].enabled = isVisible && _roleRendererInitialEnabled[i];
            }
        }

        private DungeonMasterTurretController EnsureTurretController()
        {
            var turret = GetComponent<DungeonMasterTurretController>();
            turret.Initialize(this);
            return turret;
        }

        private DungeonMasterTrapController EnsureTrapController()
        {
            var trapController = GetComponent<DungeonMasterTrapController>();
            trapController.Initialize(this);
            return trapController;
        }

        private DungeonMasterNemesisController EnsureNemesisController()
        {
            var nemesisController = GetComponent<DungeonMasterNemesisController>();
            nemesisController.Initialize(this);
            return nemesisController;
        }

        private void EnsureDoorInteractor()
        {
            var doorInteractor = GetComponent<DungeonMasterDoorInteractor>();
            if (doorInteractor == null)
            {
                doorInteractor = gameObject.AddComponent<DungeonMasterDoorInteractor>();
            }

            doorInteractor.Initialize(this);
        }

        [ClientRpc]
        public void RpcPlayHealCircleEffect(Vector3 center, float radius, int pulses, float interval)
        {
            SurvivorAbilityVfx.SpawnHealCircle(center, radius, pulses, interval);
        }

        [ClientRpc]
        public void RpcPlaySteroidEffect(uint playerNetId, float duration)
        {
            if (!NetworkClient.spawned.TryGetValue(playerNetId, out NetworkIdentity identity))
            {
                return;
            }

            GameplayPlayer target = identity.GetComponentInChildren<GameplayPlayer>();
            if (target == null)
            {
                return;
            }

            SurvivorAbilityVfx.SpawnSteroidAura(target.transform, duration);
        }

        [ClientRpc]
        public void RpcPlayEmpEffect(Vector3 center, float radius)
        {
            SurvivorAbilityVfx.SpawnEmp(center, radius);
        }

        [Server]
        private void RefreshSpeedMultiplier()
        {
            _speedMultiplier = Mathf.Max(0f, _slowMultiplier * _speedBoostMultiplier);
            if (connectionToClient != null)
            {
                TargetSetSpeedMultiplier(connectionToClient, _speedMultiplier);
            }
        }

        private IEnumerator ExpireSpeedBoost(float duration)
        {
            yield return new WaitForSeconds(duration);
            _speedBoostMultiplier = 1f;
            _speedBoostCoroutine = null;
            RefreshSpeedMultiplier();
        }
    }
}
