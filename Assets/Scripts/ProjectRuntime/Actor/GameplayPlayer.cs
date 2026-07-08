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
        private Coroutine _downedPresentationCoroutine;

        // True once this survivor has permanently died and become a ghost. Set when the ghost body is
        // configured — which happens inside DeadState.OnEnter, before the state machine assigns
        // currentState — so ghost visibility can be applied without depending on that timing.
        private bool _isGhost;

        private float _speedMultiplier = 1f;
        private int _slowStackCount;

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
        private Renderer[] _roleRenderers;
        private bool[] _roleRendererInitialEnabled;
        private bool _initialColliderEnabled;
        private bool _initialRigidbodyUseGravity;
        private bool _initialRigidbodyIsKinematic;
        private bool _cachedRoleDefaults;
        private Coroutine _deadBodyTransitionCoroutine;

        public float SpeedMultiplier => _speedMultiplier;
        public bool IsInactive => currentState is BaseInactiveState;
        public bool IsBearTrapped => currentState is BearTrappedState;
        public bool IsDowned => currentState is DownedState;
        public bool IsDead => currentState is DeadState;

        // Set the moment a survivor becomes a ghost (before currentState flips), so it stays reliable
        // even mid-transition. Used to keep ghosts from blocking shots and to drive ghost visibility.
        public bool IsGhost => _isGhost;
        public bool IsDungeonMaster => _currentRole == PlayerRole.DungeonMaster;
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

            if (!isLocalPlayer)
            {
                return;
            }

            if (isServer)
            {
                RpcPlayJumpVisual();
                return;
            }

            CmdPlayJumpVisual();
        }

        [Command]
        private void CmdPlayJumpVisual()
        {
            RpcPlayJumpVisual();
        }

        [ClientRpc(includeOwner = false)]
        private void RpcPlayJumpVisual()
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

            if (isLocalPlayer)
            {
                ClientTickDungeonMasterJumpHotkeys();
            }
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
        public void EnterGhostBody()
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
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
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

        public void SpawnCorpse(Vector3 position, Quaternion rotation)
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

        public void BeginDeadBodyTransition(Vector3 position, Quaternion rotation, float delay)
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
        public void RefreshGhostVisibility()
        {
            if (!_isGhost)
            {
                return;
            }

            bool canSee = LocalViewerCanSeeGhosts();
            GetComponent<PlayerVisualAnimator>().SetGhostVisible(canSee);
            localManager?.RefreshGhostNameVisibility(canSee);
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
        public static void RefreshAllGhostVisibility()
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

            // On their last life even a successful revive (−1 life) would leave them at 0, so skip the
            // downed/revive window and die outright — no point making a teammate revive a friend who dies
            // anyway.
            if (localManager != null && localManager.lives <= 1)
            {
                ServerDieOutright(sourceNetId);
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

            BattleManager.Instance?.ServerReportSurvivorDowned(localManager, sourceNetId);
            BattleManager.Instance?.ServerRefreshSurvivorDefeatState();
        }

        // Permanently kills the survivor without a downed/revive window (used when they're already on
        // their last life). Spends the final life and sends everyone into the ghost state.
        [Server]
        private void ServerDieOutright(uint sourceNetId)
        {
            localManager?.ServerLoseLives(1);
            BattleManager.Instance?.ServerReportSurvivorDied(localManager, sourceNetId);
            RpcEnterDeadState(transform.position);
            BattleManager.Instance?.ServerRefreshSurvivorDefeatState();
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
                // Window expired with no revive: costs 2 lives.
                ServerResolveDowned(2);
            }
        }

        // Called on the reviving survivor; downedNetId identifies the teammate being revived. Sent
        // each FixedUpdate while the reviver holds Interact in range, mirroring CmdMashBearTrap.
        [Command]
        public void CmdReviveTeammate(uint downedNetId)
        {
            if (IsDungeonMaster || IsInactive)
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

            target.ServerRegisterReviveContact();
        }

        // Called on the downed survivor each time a teammate channels a valid revive. The hold is
        // measured as continuous real-time contact: a gap longer than the grace restarts the streak,
        // and reviveHoldTime seconds of unbroken contact completes the revive.
        [Server]
        public void ServerRegisterReviveContact()
        {
            if (!IsDowned)
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
                // Revived by a teammate: costs 1 life.
                ServerResolveDowned(1);
            }
        }

        // Single resolution point for both revive-completed and revive-window-expired. livesLost is the
        // life cost of this resolution (1 for a revive, 2 for a timeout). If it empties the survivor's
        // lives they stay permanently dead and spectate; otherwise they respawn at the start point.
        [Server]
        public void ServerResolveDowned(int livesLost)
        {
            if (!IsDowned || _downedResolved)
            {
                return;
            }

            if (NetworkTime.time < _downedPresentationEndTime)
            {
                return;
            }

            _downedResolved = true;

            localManager?.ServerLoseLives(livesLost);

            if (localManager != null && localManager.IsPermanentlyDead)
            {
                // Out of lives: stay where they fell and spectate instead of respawning.
                BattleManager.Instance?.ServerReportSurvivorDied(localManager, _downedSourceNetId);
                RpcEnterDeadState(transform.position);
            }
            else
            {
                if (health != null)
                {
                    health.ServerResetHealth();
                }

                Vector3 respawnPos = GameNetworkManager.Instance.GetStartPosition().position;
                RpcEnterRespawnState(respawnPos);
            }

            // A death may have emptied the survivor pool — re-check the DM win condition.
            BattleManager.Instance?.ServerRefreshSurvivorDefeatState();
        }

        private void ClientTickRevive()
        {
            if (IsDungeonMaster || IsInactive || input == null || !input.InteractHold)
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
                rb.position = clampedPosition;
            }

            transform.position = clampedPosition;
        }

        private static Vector3 GetDungeonMasterJumpTargetPosition(GameplayPlayer target)
        {
            if (target.currentState is DeadState deadState)
            {
                return deadState.m_anchorPosition;
            }

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
            Vector3 fireDirection
        )
        {
            Turret.ServerFire(targetNetId, hitPoint, fireDirection);
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

        [Server]
        public void ServerApplySlow(float slowAmount, float duration)
        {
            _slowStackCount++;
            float newMultiplier = Mathf.Clamp(1f - _slowStackCount * slowAmount, 0f, 1f);
            _speedMultiplier = newMultiplier;
            if (connectionToClient != null)
                TargetSetSpeedMultiplier(connectionToClient, newMultiplier);
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
            float newMultiplier = Mathf.Clamp(1f - _slowStackCount * slowAmount, 0f, 1f);
            _speedMultiplier = newMultiplier;
            if (connectionToClient != null)
                TargetSetSpeedMultiplier(connectionToClient, newMultiplier);
        }

        [Server]
        public void ServerImmobilize(float duration)
        {
            if (IsDungeonMaster || IsInactive)
                return;
            ServerApplySlow(1f, duration);
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

        [ClientRpc]
        public void RpcEnterDeadState(Vector3 deathPos)
        {
            if (IsDungeonMaster)
            {
                return;
            }

            var deadState = new DeadState(this) { m_anchorPosition = deathPos };
            QueueState(deadState);
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
    }
}
