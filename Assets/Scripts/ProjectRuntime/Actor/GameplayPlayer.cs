using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public enum CharacterMode
    {
        SHOULDER,
        AIM,
        SPECTATE
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

        [Header("Anchors")]
        [field: SerializeField] public Transform aimRig { get; private set; }

        [Header("Stats")]
        public float jumpForce = 2.5f;
        public float moveSpeed = 3f;

        public bool IsInactive => currentState is BaseInactiveState;
        public override NetworkBaseState StartState => new BaseMovementState(this);
        public override NetworkBaseState DefaultState => new BaseMovementState(this);

        protected override void Awake()
        {
            base.Awake();
        }

        public override void NetworkStart()
        {
            base.NetworkStart();
        }

        protected override void FixedUpdate()
        {
            base.FixedUpdate();
            if (isServer && transform.position.y < -10f)
            {
                ServerApplyDeath();
            }
        }

        [Server]
        public void ServerApplyDeath()
        {
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
            Vector3 respawnPos = GameNetworkManager.Instance.GetStartPosition().position;
            RpcEnterRespawnState(respawnPos);
        }
        [ClientRpc]
        public void RpcEnterRespawnState(Vector3 respawnPos)
        {
            var respawnState = new RespawnState(this)
            {
                m_respawnPos = respawnPos
            };
            QueueState(respawnState);
        }
    }
}
