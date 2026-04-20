using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRuntime.Actor
{
    public class PlayerInput : NetworkBehaviour
    {
        [field: SerializeField]
        private Player Player { get; set; }

        // Exposed Vectors
        public Vector3 MoveVector;
        public Vector2 AimVector;

        // Booleans
        public bool JumpPress;

        public bool ClickPress;
        public bool ClickHold;
        public bool ClickRelease;

        public bool InteractPress;
        public bool InteractHold;
        public bool InteractRelease;

        // Actions
        public InputAction MoveInput;
        public InputAction AimInput;
        public InputAction JumpInput;

        public InputAction ClickInput;

        public InputAction InteractInput;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            this.MoveInput.Enable();
            this.AimInput.Enable();
            this.JumpInput.Enable();
            this.ClickInput.Enable();
            this.InteractInput.Enable();
        }

        private void Update()
        {
            if (!this.isLocalPlayer)
            {
                return;
            }

            this.MoveVector = this.MoveInput.ReadValue<Vector2>();
            this.AimVector = this.AimInput.ReadValue<Vector2>() * this.Player.PlayerSensitivity;

            this.JumpPress = this.JumpInput.WasPressedThisFrame();

            this.ClickPress = this.ClickInput.WasPressedThisFrame();
            this.ClickHold = this.ClickInput.IsPressed();
            this.ClickRelease = this.ClickInput.WasReleasedThisFrame();

            this.InteractPress = this.InteractInput.WasPressedThisFrame();
            this.InteractHold = this.InteractInput.IsPressed();
            this.InteractRelease = this.InteractInput.WasReleasedThisFrame();
        }
    }
}