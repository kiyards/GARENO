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
            if (player.IsDungeonMaster)
            {
                player.cam.SetCam(CharacterMode.TOP_DOWN);
                return;
            }

            player.cam.SetCam(CharacterMode.AIM);
        }

        public override void Update()
        {
            base.Update();
            if (player.IsDungeonMaster)
            {
                return;
            }

            if (!_requestedJump)
                _requestedJump = player.input.jump;
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            if (player.IsDungeonMaster)
            {
                return;
            }

            RotateAim();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (player.IsDungeonMaster)
            {
                return;
            }

            Vector3 moveDir = GetMoveDir(player.input.moveVec);
            Vector3 moveDelta = player.moveSpeed * Time.fixedDeltaTime * moveDir;
            player.rb.MovePosition(player.rb.position + moveDelta);
        }

        public override void StateUpdate()
        {
            base.StateUpdate();
            if (player.IsDungeonMaster)
            {
                return;
            }

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

    public class DungeonMasterMovementState : PlayerState
    {
        public DungeonMasterMovementState(GameplayPlayer sm) : base(sm) { }

        public override void OnEnter()
        {
            base.OnEnter();
            player.cam.SetCam(CharacterMode.TOP_DOWN);
        }

        public override void Update()
        {
            base.Update();
            if (!player.isLocalPlayer || player.input == null)
            {
                return;
            }

            if (player.input.InteractPress)
            {
                Vector3 spawnPos = player.transform.position;
                if (CursorPlacementUtility.TryGetPlacementFromCursor(500f, Physics.DefaultRaycastLayers, out Vector3 cursorPos, out _))
                {
                    spawnPos = cursorPos;
                }

                player.QueueState(new DungeonMasterTurretState(player)
                {
                    m_anchorPosition = spawnPos,
                    m_hasAnchor = true
                });
            }

            if (player.input.BearTrapPress)
            {
                player.BearTrapController.TryPlace();
            }
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!player.isLocalPlayer && !player.isServer)
            {
                return;
            }

            if (player.nextState is DungeonMasterTurretState)
            {
                return;
            }

            Vector3 moveInput = player.input != null ? player.input.MoveVector : Vector3.zero;
            Vector3 horizontalMove = Vector3.ClampMagnitude(
                new Vector3(moveInput.x, 0f, moveInput.z),
                1f) * player.DungeonMasterHorizontalSpeed;

            float verticalInput = 0f;
            if (player.input != null)
            {
                if (player.input.JumpHold)
                {
                    verticalInput += 1f;
                }

                if (player.input.FlyDownHold)
                {
                    verticalInput -= 1f;
                }
            }

            Vector3 moveDelta = (horizontalMove + Vector3.up * verticalInput * player.DungeonMasterVerticalSpeed) *
                Time.fixedDeltaTime;
            Vector3 nextPosition = player.ClampDungeonMasterPosition(player.transform.position + moveDelta);

            if (player.rb != null)
            {
                nextPosition = player.ClampDungeonMasterPosition(player.rb.position + moveDelta);
                player.rb.MovePosition(nextPosition);
                return;
            }

            player.transform.position = nextPosition;
        }
    }

    public class DungeonMasterTurretState : PlayerState
    {
        public Vector3 m_anchorPosition;
        public bool m_hasAnchor;

        public DungeonMasterTurretState(GameplayPlayer sm) : base(sm) { }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(m_anchorPosition);
            writer.WriteByte(m_hasAnchor ? (byte)1 : (byte)0);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            m_anchorPosition = reader.Read<Vector3>();
            m_hasAnchor = reader.ReadByte() != 0;
        }

        public override void OnEnter()
        {
            base.OnEnter();
            if (player.isLocalPlayer && player.input != null)
            {
                player.input.SetCursorLockOverride(true);
            }

            if (!m_hasAnchor)
            {
                m_anchorPosition = player.transform.position;
                m_hasAnchor = true;
            }

            m_anchorPosition = player.ClampDungeonMasterPosition(m_anchorPosition);
            StopMovement();
            player.Turret.Enter(m_anchorPosition);

            if (player.cam != null)
            {
                player.cam.SetCam(CharacterMode.AIM);
                player.Turret.UpdateAimFromCursor();
            }
        }

        public override void Update()
        {
            base.Update();
            if (!player.isLocalPlayer || player.input == null)
            {
                return;
            }

            if (player.input.InteractPress)
            {
                player.QueueState(new DungeonMasterMovementState(player));
                return;
            }

            if (player.input.ClickHold)
            {
                player.Turret.TryFire();
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            if (!player.isLocalPlayer)
            {
                return;
            }

            player.Turret.UpdateAimFromCursor();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!player.isLocalPlayer && !player.isServer)
            {
                return;
            }

            StopMovement();
        }

        public override void OnExit()
        {
            base.OnExit();
            if (player.isLocalPlayer && player.input != null)
            {
                player.input.SetCursorLockOverride(false);
            }

            player.Turret.Exit();
        }

        private void StopMovement()
        {
            if (player.rb != null)
            {
                player.rb.linearVelocity = Vector3.zero;
                player.rb.angularVelocity = Vector3.zero;
                player.rb.MovePosition(m_anchorPosition);
                return;
            }

            player.transform.position = m_anchorPosition;
        }
    }

    public abstract class BaseInactiveState : PlayerState
    {
        protected BaseInactiveState(GameplayPlayer sm) : base(sm) { }
    }

    public class BearTrappedState : PlayerState
    {
        public uint m_trapNetId;
        public Vector3 m_anchorPosition;

        public BearTrappedState(GameplayPlayer sm) : base(sm) { }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.WriteUInt(m_trapNetId);
            writer.Write(m_anchorPosition);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            m_trapNetId = reader.ReadUInt();
            m_anchorPosition = reader.Read<Vector3>();
        }

        public override void OnEnter()
        {
            base.OnEnter();
            if (player.cam != null)
            {
                player.cam.SetCam(CharacterMode.AIM);
            }

            StopMovement();
        }

        public override void Update()
        {
            base.Update();
            if (!player.isLocalPlayer || player.input == null)
            {
                return;
            }

            if (player.input.InteractPress)
            {
                player.CmdMashBearTrap(m_trapNetId);
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            if (!player.isLocalPlayer)
            {
                return;
            }

            RotateAim();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!player.isLocalPlayer && !player.isServer)
            {
                return;
            }

            StopMovement();
        }

        private void StopMovement()
        {
            if (player.rb != null)
            {
                player.rb.linearVelocity = Vector3.zero;
                player.rb.angularVelocity = Vector3.zero;
                player.rb.MovePosition(m_anchorPosition);
                return;
            }

            player.transform.position = m_anchorPosition;
        }

        private void RotateAim()
        {
            if (player.cam == null || player.aimRig == null)
            {
                return;
            }

            player.transform.localEulerAngles = new Vector3(0f, player.cam.transform.localEulerAngles.y, 0f);
            player.aimRig.localEulerAngles = new Vector3(player.cam.transform.localEulerAngles.x, 0f, 0f);
        }
    }

    // A survivor at 0 HP is downed rather than killed outright: immobilized at the spot they fell,
    // waiting for a teammate to revive them or for the revive window to expire. The revive timer and
    // hold progress are server-authoritative and live on GameplayPlayer; this state only replicates
    // the anchor position and pins the body there. Resolution (revive or timeout) routes through the
    // existing RespawnState, so this state has no exit logic of its own.
    public class DownedState : BaseInactiveState
    {
        public Vector3 m_anchorPosition;

        public DownedState(GameplayPlayer sm) : base(sm) { }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(m_anchorPosition);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            m_anchorPosition = reader.Read<Vector3>();
        }

        public override void OnEnter()
        {
            base.OnEnter();
            StopMovement();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!player.isLocalPlayer && !player.isServer)
            {
                return;
            }

            StopMovement();
        }

        private void StopMovement()
        {
            if (player.rb != null)
            {
                player.rb.linearVelocity = Vector3.zero;
                player.rb.angularVelocity = Vector3.zero;
                player.rb.MovePosition(m_anchorPosition);
                return;
            }

            player.transform.position = m_anchorPosition;
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
            // Teleport to the spawn point immediately (no lerp). Snap the transform on every peer so
            // remote views jump too; drive the rigidbody on the local (authority) player.
            player.transform.position = m_respawnPos;
            player.transform.rotation = Quaternion.identity;
            if (player.isLocalPlayer && player.rb != null)
            {
                player.rb.isKinematic = true;
                player.rb.position = m_respawnPos;
                player.rb.rotation = Quaternion.identity;
                player.rb.linearVelocity = Vector3.zero;
                player.rb.angularVelocity = Vector3.zero;
            }
        }
        public override void OnExit()
        {
            base.OnExit();
            player.transform.position = m_respawnPos;
            player.transform.rotation = Quaternion.identity;
            if (player.isLocalPlayer && player.rb != null)
            {
                player.rb.isKinematic = false;
            }
        }
    }
}
