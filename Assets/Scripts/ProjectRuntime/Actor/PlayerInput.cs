using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRuntime.Actor
{
    public class PlayerInput : NetworkBehaviour
    {
        [SerializeField] private GameplayPlayer player;

        public float sensitivity = 50f;
        [SyncVar] public Vector3 moveVec;
        [SyncVar] public Vector2 aimVec;

        [SyncVar] public bool jump;
        [SyncVar] public bool jumpHold;
        [SyncVar] public bool flyDownHold;

        [SyncVar] public bool clickPress;
        [SyncVar] public bool clickHold;
        [SyncVar] public bool clickRelease;

        [SyncVar] public bool interactPress;
        [SyncVar] public bool interactHold;
        [SyncVar] public bool interactRelease;

        public Vector3 MoveVector => moveVec;
        public Vector2 AimVector => aimVec;
        public bool JumpPress => jump;
        public bool JumpHold => jumpHold;
        public bool FlyDownHold => flyDownHold;
        public bool ClickPress => clickPress;
        public bool ClickHold => clickHold;
        public bool ClickRelease => clickRelease;
        public bool InteractPress => interactPress;
        public bool InteractHold => interactHold;
        public bool InteractRelease => interactRelease;

        public InputAction moveInput;
        public InputAction aimInput;
        public InputAction jumpInput;
        public InputAction clickInput;
        public InputAction interactInput;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            CacheComponents();
            SetInputEnabled(true);
        }

        private void OnDisable()
        {
            SetInputEnabled(false);
        }

        private void Update()
        {
            if (!isLocalPlayer)
                return;

            Vector2 inputVec = moveInput != null ? moveInput.ReadValue<Vector2>() : Vector2.zero;
            moveVec = new Vector3(inputVec.x, 0f, inputVec.y);
            aimVec = aimInput != null ? aimInput.ReadValue<Vector2>() * sensitivity : Vector2.zero;

            jump = jumpInput != null && jumpInput.WasPerformedThisFrame();
            jumpHold = jumpInput != null && jumpInput.IsPressed();
            flyDownHold = Keyboard.current != null &&
                (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

            clickPress = clickInput != null && clickInput.WasPressedThisFrame();
            clickHold = clickInput != null && clickInput.IsPressed();
            clickRelease = clickInput != null && clickInput.WasReleasedThisFrame();

            interactPress = interactInput != null && interactInput.WasPressedThisFrame();
            interactHold = interactInput != null && interactInput.IsPressed();
            interactRelease = interactInput != null && interactInput.WasReleasedThisFrame();
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
                return;
            }

            moveInput?.Disable();
            aimInput?.Disable();
            jumpInput?.Disable();
            clickInput?.Disable();
            interactInput?.Disable();

            moveVec = Vector3.zero;
            aimVec = Vector2.zero;
            jump = false;
            jumpHold = false;
            flyDownHold = false;
            clickPress = false;
            clickHold = false;
            clickRelease = false;
            interactPress = false;
            interactHold = false;
            interactRelease = false;
        }

        private void CacheComponents()
        {
            player ??= GetComponentInParent<GameplayPlayer>();
            player ??= transform.root.GetComponentInChildren<GameplayPlayer>(true);
        }
    }
}
