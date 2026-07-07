using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRuntime.Actor
{
    public class PlayerInput : NetworkBehaviour
    {
        [SerializeField]
        private GameplayPlayer player;

        [SerializeField]
        private bool lockCursorOnStart = true;
        private bool _shouldLockCursorForRole;
        private bool _shouldForceCursorLock;

        public float sensitivity = 50f;

        [SyncVar]
        public Vector3 moveVec;

        [SyncVar]
        public Vector2 aimVec;

        [SyncVar]
        public bool jump;

        [SyncVar]
        public bool jumpHold;

        [SyncVar]
        public bool flyDownHold;

        [SyncVar]
        public bool clickPress;

        [SyncVar]
        public bool clickHold;

        [SyncVar]
        public bool clickRelease;

        // Right mouse button, synced (unlike the card-placement-cancel right-click read, which is
        // local-only and doesn't need server validation). Currently used to trigger Nemesis Lunge.
        [SyncVar]
        public bool rightClickPress;

        [SyncVar]
        public bool interactPress;

        [SyncVar]
        public bool interactHold;

        [SyncVar]
        public bool interactRelease;

        [SyncVar]
        public bool reloadPress;


        public Vector3 MoveVector => moveVec;
        public Vector2 AimVector => aimVec;
        public bool JumpPress => jump;
        public bool JumpHold => jumpHold;
        public bool FlyDownHold => flyDownHold;
        public bool ClickPress => clickPress;
        public bool ClickHold => clickHold;
        public bool ClickRelease => clickRelease;
        public bool RightClickPress => rightClickPress;
        public bool InteractPress => interactPress;
        public bool InteractHold => interactHold;
        public bool InteractRelease => interactRelease;
        public bool ReloadPress => reloadPress;
        public bool TurretExitPress => Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
        public bool TeammateIndicatorsTogglePress => Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;

        public bool TryGetDungeonMasterJumpSlot(out int slotIndex)
        {
            slotIndex = -1;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            if (keyboard.digit1Key.wasPressedThisFrame)
            {
                slotIndex = 0;
                return true;
            }

            if (keyboard.digit2Key.wasPressedThisFrame)
            {
                slotIndex = 1;
                return true;
            }

            if (keyboard.digit3Key.wasPressedThisFrame)
            {
                slotIndex = 2;
                return true;
            }

            if (keyboard.digit4Key.wasPressedThisFrame)
            {
                slotIndex = 3;
                return true;
            }

            return false;
        }

        public InputAction moveInput;
        public InputAction aimInput;
        public InputAction jumpInput;
        public InputAction clickInput;
        public InputAction interactInput;
        public InputAction reloadInput;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            CacheComponents();
            SetInputEnabled(true);
            SetCursorLockedForRole(
                player != null && player.localManager != null
                    ? player.localManager.playerRole
                    : PlayerRole.Unassigned
            );
        }

        private void OnDisable()
        {
            SetInputEnabled(false);
            ReleaseCursorLock();
        }

        private void Update()
        {
            if (!isLocalPlayer)
                return;

            RefreshCursorLock();

            Vector2 inputVec = moveInput != null ? moveInput.ReadValue<Vector2>() : Vector2.zero;
            moveVec = new Vector3(inputVec.x, 0f, inputVec.y);
            aimVec = aimInput != null ? aimInput.ReadValue<Vector2>() * sensitivity : Vector2.zero;

            jump = jumpInput != null && jumpInput.WasPerformedThisFrame();
            jumpHold = jumpInput != null && jumpInput.IsPressed();
            flyDownHold =
                Keyboard.current != null
                && (
                    Keyboard.current.leftShiftKey.isPressed
                    || Keyboard.current.rightShiftKey.isPressed
                );

            clickPress = clickInput != null && clickInput.WasPressedThisFrame();
            clickHold = clickInput != null && clickInput.IsPressed();
            clickRelease = clickInput != null && clickInput.WasReleasedThisFrame();
            rightClickPress = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;

            interactPress = interactInput != null && interactInput.WasPressedThisFrame();
            interactHold = interactInput != null && interactInput.IsPressed();
            interactRelease = interactInput != null && interactInput.WasReleasedThisFrame();

            reloadPress = reloadInput != null && reloadInput.WasPressedThisFrame();
        }

        private void SetInputEnabled(bool isEnabled)
        {
            if (isEnabled)
            {
                moveInput?.Enable();
                aimInput?.Enable();
                jumpInput?.Enable();
                clickInput?.Enable();
                interactInput?.Enable();
                reloadInput?.Enable();
                return;
            }

            moveInput?.Disable();
            aimInput?.Disable();
            jumpInput?.Disable();
            clickInput?.Disable();
            interactInput?.Disable();
            reloadInput?.Disable();

            moveVec = Vector3.zero;
            aimVec = Vector2.zero;
            jump = false;
            jumpHold = false;
            flyDownHold = false;
            clickPress = false;
            clickHold = false;
            clickRelease = false;
            rightClickPress = false;
            interactPress = false;
            interactHold = false;
            interactRelease = false;
            reloadPress = false;
        }

        private void CacheComponents()
        {
            player ??= GetComponentInParent<GameplayPlayer>();
            player ??= transform.root.GetComponentInChildren<GameplayPlayer>(true);
        }

        public void SetCursorLockedForRole(PlayerRole role)
        {
            _shouldLockCursorForRole = role == PlayerRole.Survivor;
            RefreshCursorLock();
        }

        public void SetCursorLockOverride(bool shouldLock)
        {
            _shouldForceCursorLock = shouldLock;
            RefreshCursorLock();
        }

        private void RefreshCursorLock()
        {
            if (!isLocalPlayer)
            {
                return;
            }

            if (lockCursorOnStart && (_shouldLockCursorForRole || _shouldForceCursorLock))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                return;
            }

            ReleaseCursorLock();
        }

        private bool ShouldLockCursor()
        {
            return _shouldLockCursorForRole
                || (
                    player != null
                    && player.IsDungeonMaster
                    && player.currentState is DungeonMasterTurretState
                );
        }

        private void ReleaseCursorLock()
        {
            if (!isLocalPlayer || !lockCursorOnStart)
            {
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
