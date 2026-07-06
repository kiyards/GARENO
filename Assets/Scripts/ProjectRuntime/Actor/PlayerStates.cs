using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;
using static UnityEngine.RuleTile.TilingRuleOutput;

namespace ProjectRuntime.Actor.PlayerStates
{
    public abstract class PlayerState : NetworkBaseState
    {
        protected GameplayPlayer player;

        protected PlayerState(GameplayPlayer sm)
            : base(sm)
        {
            player = sm;
        }
    }

    public class BaseMovementState : PlayerState
    {
        private bool _requestedJump;

        public BaseMovementState(GameplayPlayer sm)
            : base(sm) { }

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
            Vector3 moveDelta =
                player.moveSpeed * player.SpeedMultiplier * Time.fixedDeltaTime * moveDir;
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
            if (!player.isLocalPlayer)
                return;
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
            player.transform.localEulerAngles = new Vector3(
                0f,
                player.cam.transform.localEulerAngles.y,
                0f
            );
            player.aimRig.localEulerAngles = new Vector3(
                player.cam.transform.localEulerAngles.x,
                0f,
                0f
            );
        }

        public Vector3 GetMoveDir(Vector3 inputVec)
        {
            Vector3 up =
                player.groundCheck != null ? player.groundCheck.GroundedNormal : Vector3.up;
            Vector3 aimForwardFlat =
                Quaternion.Euler(0f, player.cam.transform.eulerAngles.y, 0f) * Vector3.forward;

            Vector3 forward = Vector3.ProjectOnPlane(aimForwardFlat, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            return Vector3.ClampMagnitude(right * inputVec.x + forward * inputVec.z, 1f);
        }
    }

    public class DungeonMasterMovementState : PlayerState
    {
        public DungeonMasterMovementState(GameplayPlayer sm)
            : base(sm) { }

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

            if (player.Turret.IsAssembling)
            {
                return;
            }

            Vector3 moveInput = player.input != null ? player.input.MoveVector : Vector3.zero;
            Vector3 horizontalMove =
                Vector3.ClampMagnitude(new Vector3(moveInput.x, 0f, moveInput.z), 1f)
                * player.DungeonMasterHorizontalSpeed;

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

            Vector3 moveDelta =
                (horizontalMove + Vector3.up * verticalInput * player.DungeonMasterVerticalSpeed)
                * Time.fixedDeltaTime;
            Vector3 nextPosition = player.ClampDungeonMasterPosition(
                player.transform.position + moveDelta
            );

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

        public DungeonMasterTurretState(GameplayPlayer sm)
            : base(sm) { }

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
                if (PlayerHudManager.Instance != null)
                    PlayerHudManager.Instance.SetTurretModeActive(true);
            }

            if (!m_hasAnchor)
            {
                m_anchorPosition = player.transform.position;
                m_hasAnchor = true;
            }
            else if (player.isLocalPlayer)
            {
                player.Turret.ClientStartTurretLifetime();
            }

            m_anchorPosition = player.ClampDungeonMasterPosition(m_anchorPosition);
            StopMovement();
            player.Turret.Enter(m_anchorPosition);

            if (player.cam != null)
            {
                player.cam.SetCam(CharacterMode.AIM);
                GameplayPlayer closest = FindClosestSurvivor(player.transform.position);
                if (closest != null)
                    player.cam.LookTowards(closest.transform.position);
                else
                    player.cam.LookTowards(player.transform.position + player.transform.forward);
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

            if (player.input.TurretExitPress && !player.Turret.IsDisassembling)
            {
                player.CmdBeginTurretDisassemble();
                return;
            }

            if (player.input.ClickHold && !player.Turret.IsDisassembling)
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
                if (PlayerHudManager.Instance != null)
                    PlayerHudManager.Instance.SetTurretModeActive(false);
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

        private GameplayPlayer FindClosestSurvivor(Vector3 from)
        {
            GameplayPlayer closest = null;
            float closestSqDist = float.MaxValue;
            foreach (var gp in UnityEngine.Object.FindObjectsByType<GameplayPlayer>(FindObjectsSortMode.None))
            {
                if (gp == player || gp.IsDungeonMaster || gp.IsGhost || gp.IsInactive)
                    continue;
                float sqDist = (gp.transform.position - from).sqrMagnitude;
                if (sqDist < closestSqDist)
                {
                    closestSqDist = sqDist;
                    closest = gp;
                }
            }
            return closest;
        }
    }

    // The Dungeon Master possessing the Nemesis. Mirrors DungeonMasterTurretState's control-handover
    // shape, but the possessed entity moves: this state drives the (client-owned) Nemesis from the DM's
    // camera-relative input and frames it in a 3rd-person (SHOULDER) camera via the spectate target.
    // The state has no duration — the Nemesis entity's own 60s lifetime (or an early end) destroys it,
    // and DungeonMasterNemesisController.DetachSpawnedNemesis queues the return to placement.
    public class DungeonMasterNemesisState : PlayerState
    {
        public DungeonMasterNemesisState(GameplayPlayer sm)
            : base(sm) { }

        public override void OnEnter()
        {
            base.OnEnter();
            if (!player.isLocalPlayer || player.cam == null)
            {
                return;
            }

            var nemesis = player.Nemesis.ActiveNemesis;
            if (nemesis != null)
            {
                player.cam.SetSpectateTarget(nemesis.NemesisRoot);
            }

            player.cam.SetCam(CharacterMode.SHOULDER);
        }

        public override void Update()
        {
            base.Update();
            if (!player.isLocalPlayer || player.input == null)
            {
                return;
            }

            // Early end reuses the turret's exit key (T).
            if (player.input.TurretExitPress && !player.Nemesis.IsDisassembling)
            {
                player.CmdEndNemesisEarly();
                return;
            }

            var nemesis = player.Nemesis.ActiveNemesis;
            if (nemesis == null)
            {
                return;
            }

            // Punch = left click, Lunge = right click, Ground Slam = E. Availability is checked against
            // the already-replicated cooldown SyncVars — no client clock needed (see IsAttackAvailable).
            if (player.input.ClickPress)
            {
                TryAttack(nemesis, NemesisAttackType.Punch);
            }
            else if (player.input.RightClickPress)
            {
                TryAttack(nemesis, NemesisAttackType.Lunge);
            }
            else if (player.input.InteractPress)
            {
                TryAttack(nemesis, NemesisAttackType.GroundSlam);
            }
        }

        private void TryAttack(DungeonMasterNemesis nemesis, NemesisAttackType type)
        {
            if (!nemesis.IsAttackAvailable(type))
            {
                return;
            }

            // Drive the forward lunge dash locally: the owner is authoritative over the Nemesis's
            // NetworkTransform, so kicking it off here (rather than waiting on server confirmation)
            // keeps the motion in step with the client-authoritative movement model. The same
            // IsAttackAvailable gate the server re-checks guards against firing without a real lunge.
            if (type == NemesisAttackType.Lunge)
            {
                nemesis.OwnerBeginLunge();
            }

            player.CmdNemesisAttack((int)type);
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            if (!player.isLocalPlayer)
            {
                return;
            }

            var nemesis = player.Nemesis.ActiveNemesis;
            if (nemesis == null)
            {
                return;
            }

            Vector3 input = player.input != null ? player.input.moveVec : Vector3.zero;
            nemesis.OwnerMove(GetCameraRelativeMoveDir(input));
        }

        public override void OnExit()
        {
            base.OnExit();
            if (!player.isLocalPlayer || player.cam == null)
            {
                return;
            }

            player.cam.ClearSpectateTarget();
            player.cam.SetCam(CharacterMode.TOP_DOWN);
        }

        private Vector3 GetCameraRelativeMoveDir(Vector3 inputVec)
        {
            if (player.cam == null)
            {
                return new Vector3(inputVec.x, 0f, inputVec.z);
            }

            Vector3 forward =
                Quaternion.Euler(0f, player.cam.transform.eulerAngles.y, 0f) * Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            return Vector3.ClampMagnitude(right * inputVec.x + forward * inputVec.z, 1f);
        }
    }

    public abstract class BaseInactiveState : PlayerState
    {
        protected BaseInactiveState(GameplayPlayer sm)
            : base(sm) { }
    }

    public class BearTrappedState : PlayerState
    {
        public uint m_trapNetId;
        public Vector3 m_anchorPosition;

        private BearTrap _trap;

        public BearTrappedState(GameplayPlayer sm)
            : base(sm) { }

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

            if (player.isLocalPlayer)
            {
                if (NetworkClient.spawned.TryGetValue(m_trapNetId, out var identity))
                    _trap = identity?.GetComponent<BearTrap>();

                PlayerHudManager.Instance?.SetBearTrapBarActive(true);
            }
        }

        public override void OnExit()
        {
            base.OnExit();
            if (player.isLocalPlayer)
            {
                PlayerHudManager.Instance?.SetBearTrapBarActive(false);
                _trap = null;
            }
        }

        public override void Update()
        {
            base.Update();
            if (!player.isLocalPlayer || player.input == null)
            {
                return;
            }

            PlayerHudManager.Instance?.SetBearTrapEscapeFill(_trap != null ? _trap.MashProgress : 0f);

            if (player.input.InteractPress)
            {
                player.CmdMashBearTrap(m_trapNetId);
                PlayerHudManager.Instance?.TriggerBearTrapShake(_trap != null ? _trap.MashProgress : 0f);
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

            player.transform.localEulerAngles = new Vector3(
                0f,
                player.cam.transform.localEulerAngles.y,
                0f
            );
            player.aimRig.localEulerAngles = new Vector3(
                player.cam.transform.localEulerAngles.x,
                0f,
                0f
            );
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

        // Total time from entering this state until the revive window expires (presentation delay +
        // GameplayPlayer.ReviveWindow), set once server-side in ServerEnterDowned. Combined with the
        // base class's replicated elapsedTime, this lets clients display a countdown without
        // duplicating the server's timeout calculation.
        public float totalDuration;

        public DownedState(GameplayPlayer sm)
            : base(sm) { }

        public override void OnSerialize(NetworkWriter writer)
        {
            base.OnSerialize(writer);
            writer.Write(m_anchorPosition);
            writer.Write(totalDuration);
        }

        public override void OnDeserialize(NetworkReader reader)
        {
            base.OnDeserialize(reader);
            m_anchorPosition = reader.Read<Vector3>();
            totalDuration = reader.ReadFloat();
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

        public RespawnState(GameplayPlayer sm)
            : base(sm)
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

    // A survivor who has spent all their lives is permanently dead. After the death presentation they
    // become a "ghost" that still walks and collides with the world exactly like a living player,
    // except it passes through other players (collision-ignored in EnterGhostBody). The ghost is not
    // revivable (revive targeting only accepts IsDowned) and, being a BaseInactiveState, can't shoot and
    // is ignored by zombies and traps. It is visible only to the Dungeon Master and to other dead
    // survivors — RefreshGhostVisibility enforces that per client. Movement mirrors BaseMovementState.
    public class DeadState : BaseInactiveState
    {
        public Vector3 m_anchorPosition;
        private bool _deathPresentationQueued;
        private bool _requestedJump;

        public DeadState(GameplayPlayer sm)
            : base(sm) { }

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

            if (!_deathPresentationQueued)
            {
                var visualAnimator = player.GetComponent<PlayerVisualAnimator>();
                var deathPresentationDelay =
                    player.currentState is DownedState
                        ? 0f
                        : visualAnimator.GetDeathAnimationDuration(0f);
                player.BeginDeadBodyTransition(
                    m_anchorPosition,
                    player.transform.rotation,
                    deathPresentationDelay
                );
                _deathPresentationQueued = true;
            }

            if (player.isLocalPlayer)
            {
                if (player.cam != null)
                {
                    player.cam.SetCam(CharacterMode.AIM);
                }
            }
        }

        public override void Update()
        {
            base.Update();
            if (!_requestedJump && player.input != null)
            {
                _requestedJump = player.input.jump;
            }
        }

        public override void LateUpdate()
        {
            base.LateUpdate();
            RotateAim();
        }

        public override void FixedUpdate()
        {
            base.FixedUpdate();
            Vector3 moveDir = GetMoveDir(
                player.input != null ? player.input.moveVec : Vector3.zero
            );
            Vector3 moveDelta = player.moveSpeed * Time.fixedDeltaTime * moveDir;
            player.rb.MovePosition(player.rb.position + moveDelta);
        }

        public override void StateUpdate()
        {
            base.StateUpdate();
            if (_requestedJump)
            {
                if (player.groundCheck.IsGrounded)
                {
                    Jump();
                }
                else
                {
                    _requestedJump = false;
                }
            }
        }

        private void Jump()
        {
            if (!player.isLocalPlayer)
            {
                return;
            }

            if (player.rb.linearVelocity.y < 0)
            {
                var vel = player.rb.linearVelocity;
                vel.y = 0;
                player.rb.linearVelocity = vel;
            }

            player.rb.AddForce(Vector3.up * player.jumpForce, ForceMode.VelocityChange);
        }

        private void RotateAim()
        {
            if (!player.isLocalPlayer || player.cam == null || player.aimRig == null)
            {
                return;
            }

            player.transform.localEulerAngles = new Vector3(
                0f,
                player.cam.transform.localEulerAngles.y,
                0f
            );
            player.aimRig.localEulerAngles = new Vector3(
                player.cam.transform.localEulerAngles.x,
                0f,
                0f
            );
        }

        private Vector3 GetMoveDir(Vector3 inputVec)
        {
            Vector3 up =
                player.groundCheck != null ? player.groundCheck.GroundedNormal : Vector3.up;
            Vector3 aimForwardFlat =
                Quaternion.Euler(0f, player.cam.transform.eulerAngles.y, 0f) * Vector3.forward;

            Vector3 forward = Vector3.ProjectOnPlane(aimForwardFlat, up).normalized;
            Vector3 right = Vector3.Cross(up, forward).normalized;

            return Vector3.ClampMagnitude(right * inputVec.x + forward * inputVec.z, 1f);
        }
    }
}
