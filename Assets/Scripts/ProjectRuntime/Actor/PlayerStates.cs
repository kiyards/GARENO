using Mirror;
using ProjectRuntime.Network;
using UnityEngine;
using static UnityEngine.RuleTile.TilingRuleOutput;

namespace ProjectRuntime.Actor.PlayerStates
{
    public abstract class PlayerState : NetworkBaseState
    {
        protected GameplayPlayer player;

        protected PlayerState(GameplayPlayer sm) : base(sm)
        {
            player = sm;
        }
    }

    public class BaseMovementState : PlayerState
    {
        private bool _requestedJump;

        public BaseMovementState(GameplayPlayer sm) : base(sm) { }

        public override void OnEnter()
        {
            base.OnEnter();
            player.cam.SetCam(CharacterMode.AIM);
        }

        public override void Update()
        {
            base.Update();

            if (!_requestedJump)
                _requestedJump = player.input.jump;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            RotateAim();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            Vector3 moveDir = GetMoveDir(player.input.moveVec);
            Vector3 moveDelta = player.moveSpeed * Time.fixedDeltaTime * moveDir;
            player.rb.MovePosition(player.rb.position + moveDelta);
        }

        public override void StateUpdate()
        {
            base.StateUpdate();
            if (_requestedJump)
            {
                if (player.groundCheck.IsGrounded)
                    Jump();
                else
                    _requestedJump = false;
            }
        }

        public void Jump()
        {
            if (!player.isLocalPlayer) return;
            if (player.rb.linearVelocity.y < 0)
            {
                var vel = player.rb.linearVelocity;
                vel.y = 0;
                player.rb.linearVelocity = vel;
            }
            player.rb.AddForce(Vector3.up * player.jumpForce, ForceMode.VelocityChange);
        }

        public void RotateAim()
        {
            player.transform.localEulerAngles = new Vector3(0f, player.cam.transform.localEulerAngles.y, 0f);
            player.aimRig.localEulerAngles = new Vector3(player.cam.transform.localEulerAngles.x, 0f, 0f);
        }

        public Vector3 GetMoveDir(Vector3 inputVec)
        {
            Vector3 up = player.groundCheck != null ? player.groundCheck.GroundedNormal : Vector3.up;
            Vector3 aimForwardFlat = Quaternion.Euler(0f, player.cam.transform.eulerAngles.y, 0f) * Vector3.forward;

            Vector3 forward = Vector3.ProjectOnPlane(aimForwardFlat, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            return Vector3.ClampMagnitude(right * inputVec.x + forward * inputVec.z, 1f);
        }
    }

    public abstract class BaseInactiveState : PlayerState
    {
        protected BaseInactiveState(GameplayPlayer sm) : base(sm) { }
    }

    public class DeathState : BaseInactiveState
    {
        const float DeathTime = 2f;
        bool _requestedRespawn;
        public DeathState(GameplayPlayer sm) : base(sm) { }
        public override void OnEnter()
        {
            base.OnEnter();
            player.cam.SetCam(CharacterMode.SPECTATE);
        }

        public override void Update()
        {
            base.Update();
            if (!player.isLocalPlayer) return;
            if (elapsedTime >= DeathTime)
                _requestedRespawn = true;
        }

        public override void StateUpdate()
        {
            base.StateUpdate();
            if (_requestedRespawn)
                player.CmdEnterRespawnState();
        }
    }

    public class RespawnState : BaseInactiveState
    {
        const float RespawnTime = 0.5f;
        public Vector3 m_respawnPos;

        public RespawnState(GameplayPlayer sm) : base(sm)
        {
            duration = RespawnTime;
        }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(m_respawnPos);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            m_respawnPos = reader.Read<Vector3>();
        }
        public override void OnEnter()
        {
            base.OnEnter();
            if (player.isLocalPlayer)
            {
                player.rb.isKinematic = true;
            }
        }
        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!player.isLocalPlayer) return;
            player.transform.position = Vector3.Lerp(player.transform.position, m_respawnPos, elapsedTime / duration);
            player.transform.rotation = Quaternion.Lerp(player.transform.rotation, Quaternion.identity, elapsedTime / duration);
        }
        public override void OnExit()
        {
            base.OnExit();
            player.transform.position = m_respawnPos;
            player.transform.rotation = Quaternion.identity;
            if (player.isLocalPlayer)
            {
                player.rb.isKinematic = false;
            }
        }
    }
}
