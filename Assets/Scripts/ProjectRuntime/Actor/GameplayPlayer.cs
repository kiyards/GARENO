using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectRuntime.Actor
{
    public enum CharacterMode
    {
        SHOULDER,
        AIM,
        SPECTATE,
        TOP_DOWN
    }

    public class GameplayPlayer : NetworkStateMachine
    {
        [Header("Components")]
        [field: SerializeField] public PlayerManager localManager { get; private set; }
        [field: SerializeField] public PlayerInput input { get; private set; }
        [field: SerializeField] public CameraController cam { get; private set; }
        [field: SerializeField] public SphereGroundCheck groundCheck { get; private set; }
        [field: SerializeField] public Rigidbody rb { get; private set; }
        [field: SerializeField] public Collider col { get; private set; }
        [field: SerializeField] public Health health { get; private set; }

        [Header("Anchors")]
        [field: SerializeField] public Transform aimRig { get; private set; }

        [Header("Stats")]
        public float jumpForce = 2.5f;
        public float moveSpeed = 3f;

        [Header("Dungeon Master")]
        [SerializeField, FormerlySerializedAs("mastermindHorizontalSpeed")]
        private float dungeonMasterHorizontalSpeed = 18f;

        [SerializeField, FormerlySerializedAs("mastermindVerticalSpeed")]
        private float dungeonMasterVerticalSpeed = 12f;

        [SerializeField] private float dungeonMasterMinY = 0f;
        [SerializeField] private float dungeonMasterMaxY = 40f;

        [Header("Revive")]
        // How long a downed survivor waits before auto-resolving if no teammate revives them.
        [SerializeField] private float reviveWindow = 30f;
        // How long a teammate must hold Interact in range to revive a downed survivor.
        [SerializeField] private float reviveHoldTime = 2f;
        // How close a teammate must be to revive a downed survivor.
        [SerializeField] private float reviveRange = 2.5f;

        // If no revive contact arrives for this long, the hold streak is considered broken (the
        // reviver left range or stopped holding Interact) and the next contact starts a fresh hold.
        private const float ReviveContactGrace = 0.25f;

        private double _downedStartTime;
        // Server time (NetworkTime.time) when the current continuous revive-hold streak began, and
        // the last time a valid revive contact arrived. Hold duration is measured in real time so it
        // can't be outrun by command batching/frame-rate.
        private double _reviveContactStartTime;
        private double _lastReviveContactTime;
        private uint _downedSourceNetId;
        // Guards against re-resolving while the downed→respawn state transition round-trips back from
        // the owning client (the state authority), which would otherwise re-fire every physics frame.
        private bool _downedResolved;
        // True once this survivor has permanently died and become a ghost. Set when the ghost body is
        // configured — which happens inside DeadState.OnEnter, before the state machine assigns
        // currentState — so ghost visibility can be applied without depending on that timing.
        private bool _isGhost;

        private PlayerRole _currentRole = PlayerRole.Unassigned;
        private DungeonMasterCardManager _cardManager;
        public DungeonMasterCardManager CardManager
            => this._cardManager ??= this.GetComponent<DungeonMasterCardManager>();
        private DungeonMasterTurretController _turret;
        public DungeonMasterTurretController Turret
            => this._turret != null ? this._turret : this._turret = this.EnsureTurretController();
        private DungeonMasterBearTrapController _bearTrapController;
        public DungeonMasterBearTrapController BearTrapController
            => this._bearTrapController != null
                ? this._bearTrapController
                : this._bearTrapController = this.EnsureBearTrapController();
        private Renderer[] _roleRenderers;
        private bool[] _roleRendererInitialEnabled;
        private bool _initialColliderEnabled;
        private bool _initialRigidbodyUseGravity;
        private bool _initialRigidbodyIsKinematic;
        private bool _cachedRoleDefaults;

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
        public override NetworkBaseState StartState => IsDungeonMaster
            ? new DungeonMasterMovementState(this)
            : new BaseMovementState(this);
        public override NetworkBaseState DefaultState => IsDungeonMaster
            ? new DungeonMasterMovementState(this)
            : new BaseMovementState(this);

        protected override void Awake()
        {
            base.Awake();
            CacheRoleDefaults();
            Turret.SetVisible(false);
            BearTrapController.Initialize(this);
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

        [Server]
        private void OnHealthDepleted(uint killerNetId)
        {
            ServerEnterDowned(killerNetId);
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
        // (normal survivor physics), but passes through every other player. The model is left intact
        // (the ghost reuses it); per-viewer visibility is handled by RefreshGhostVisibility.
        public void EnterGhostBody()
        {
            CacheRoleDefaults();
            _isGhost = true;

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

        // Leaves a corpse marker where the survivor fell. The project has no character models yet, so the
        // corpse is a plain capsule (matching the player's capsule body) laid on its side, with no
        // collider. Instantiated locally on every client from the replicated death position.
        public void SpawnCorpse(Vector3 position)
        {
            var corpse = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            corpse.name = "Corpse";
            corpse.transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
            corpse.transform.localScale = transform.lossyScale;

            var corpseCollider = corpse.GetComponent<Collider>();
            if (corpseCollider != null)
            {
                Destroy(corpseCollider);
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
            SetRenderersVisible(canSee);
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

            _downedStartTime = NetworkTime.time;
            _downedSourceNetId = sourceNetId;
            // Sentinel in the past so the first contact always starts a fresh hold streak.
            _lastReviveContactTime = double.NegativeInfinity;
            _reviveContactStartTime = 0d;
            _downedResolved = false;

            ServerForceState(new DownedState(this)
            {
                m_anchorPosition = transform.position
            });

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

            if (BattleManager.Instance != null &&
                BattleManager.Instance.CurrentRoundPhase == RoundPhase.RoundComplete)
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

            if ((target.transform.position - transform.position).sqrMagnitude >
                reviveRange * reviveRange)
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

        private GameplayPlayer FindReviveTarget()
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
        public void CmdFireDungeonMasterTurret(uint targetNetId, Vector3 hitPoint)
        {
            Turret.ServerFire(targetNetId, hitPoint);
        }

        [Command]
        public void CmdBeginTurretDisassemble()
        {
            Turret.ServerBeginDisassemble();
        }

        [Command]
        public void CmdPlaceBearTrap(Vector3 position, Vector3 normal)
        {
            BearTrapController.ServerPlace(position, normal);
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
        public void ServerEnterBearTrap(BearTrap trap, Vector3 anchorPosition)
        {
            if (trap == null || IsDungeonMaster || IsInactive)
            {
                return;
            }

            ServerForceState(new BearTrappedState(this)
            {
                m_trapNetId = trap.netId,
                m_anchorPosition = anchorPosition
            });
        }

        [Server]
        public void ServerExitBearTrap(uint trapNetId)
        {
            if (!(currentState is BearTrappedState trappedState) ||
                trappedState.m_trapNetId != trapNetId)
            {
                return;
            }

            ServerForceState(new BaseMovementState(this));
        }

        [ClientRpc]
        public void RpcShowDungeonMasterTurretTracer(Vector3 hitPoint)
        {
            Turret.ShowTracer(hitPoint);
        }

        [ClientRpc]
        public void RpcEnterRespawnState(Vector3 respawnPos)
        {
            if (IsDungeonMaster)
            {
                return;
            }

            var respawnState = new RespawnState(this)
            {
                m_respawnPos = respawnPos
            };
            QueueState(respawnState);
        }

        [ClientRpc]
        public void RpcEnterDeadState(Vector3 deathPos)
        {
            if (IsDungeonMaster)
            {
                return;
            }

            var deadState = new DeadState(this)
            {
                m_anchorPosition = deathPos
            };
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
                _roleRendererInitialEnabled[i] = _roleRenderers[i] != null && _roleRenderers[i].enabled;
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

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
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

        private DungeonMasterBearTrapController EnsureBearTrapController()
        {
            var bearTrapController = GetComponent<DungeonMasterBearTrapController>();
            bearTrapController.Initialize(this);
            return bearTrapController;
        }
    }
}
