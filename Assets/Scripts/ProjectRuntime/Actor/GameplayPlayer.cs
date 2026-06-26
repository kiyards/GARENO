using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
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

        private PlayerRole _currentRole = PlayerRole.Unassigned;
        private Renderer[] _roleRenderers;
        private bool[] _roleRendererInitialEnabled;
        private bool _initialColliderEnabled;
        private bool _initialRigidbodyUseGravity;
        private bool _initialRigidbodyIsKinematic;
        private bool _cachedRoleDefaults;

        public bool IsInactive => currentState is BaseInactiveState;
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
            ServerApplyDeath();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!IsDungeonMaster && isServer && transform.position.y < -10f)
            {
                ServerApplyDeath();
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

        [Server]
        public void ServerApplyDeath()
        {
            if (IsDungeonMaster)
            {
                return;
            }

            RpcApplyDeath();
        }
        [ClientRpc]
        public void RpcApplyDeath()
        {
            if (IsInactive) return;
            QueueState(new DeathState(this));
        }

        [Command]
        public void CmdEnterRespawnState()
        {
            if (IsDungeonMaster)
            {
                return;
            }

            if (health != null)
                health.ServerResetHealth();

            Vector3 respawnPos = GameNetworkManager.Instance.GetStartPosition().position;
            RpcEnterRespawnState(respawnPos);
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
    }
}
