using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRuntime.Actor
{
    public class Player : NetworkBehaviour
    {
        public static Player Instance { get; private set; }

        [field: SerializeField, Header("Scene References")]
        private Rigidbody PlayerRigidbody { get; set; }

        [field: SerializeField]
        private Collider PlayerCollider { get; set; }

        [field: SerializeField]
        private CameraController PlayerCameraController { get; set; }

        [field: SerializeField]
        private PlayerInput PlayerInput { get; set; }

        [field: SerializeField]
        private Transform AimRig { get; set; }

        [field: SerializeField, Header("Ground Check Logic")]
        private Transform GroundCheckTransform { get; set; }

        [field: SerializeField]
        private LayerMask GroundLayerMask { get; set; }

        [field: SerializeField]
        private float GroundRadiusCheck { get; set; }

        [field: SerializeField]
        private float GroundCastDistance { get; set; }

        [field: SerializeField]
        private float GroundMaxSlopeAngle { get; set; }

        [field: SerializeField, Header("Player Settings")]
        private float PlayerMovementSpeed { get; set; }

        [field: SerializeField]
        private float PlayerJumpForce { get; set; }

        [field: SerializeField]
        public float PlayerSensitivity { get; set; } // public to get from PlayerInput

        // Internal Variables
        private Vector3 _groundNormal;
        private bool _isGrounded;
        private float _aimYaw;
        private float _aimPitch;
        private Vector3 _cameraControllerLocalOffset;
        private Vector3 _aimRigLocalOffset;
        private Vector3 _groundCheckLocalOffset;

        // Networking
        private string _playerName;
        private ulong _playerSteamId;
        private int _playerIndex;
        private bool _isCursorLocked = true;

        private void Awake()
        {
            this._aimYaw = this.PlayerRigidbody.rotation.eulerAngles.y;
            this._aimRigLocalOffset = this._cameraControllerLocalOffset;
            this._groundCheckLocalOffset = this.GroundCheckTransform.localPosition;
            this.PlayerRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            this.PlayerRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            if (Instance == null && this.isLocalPlayer)
            {
                Instance = this;
            }

            this.SetCursorState(this._isCursorLocked);
        }

        public void Init(string playerName,  ulong playerSteamId, int playerIndex)
        {
            this._playerName = playerName;
            this._playerSteamId = playerSteamId;
            this._playerIndex = playerIndex;
        }

        private void Update()
        {
            if (!this.isLocalPlayer)
            {
                return;
            }

            this.HandleCursorToggle();
            this.HandleRotateAim();
            this.IsGroundedCheck();
            if (this.PlayerInput.JumpPress && this._isGrounded)
            {
                this.HandleJump();
            }
        }

        private void FixedUpdate()
        {
            if (!this.isLocalPlayer)
            {
                return;
            }

            this.HandleMovement();
        }

        private void LateUpdate()
        {
            if (!this.isLocalPlayer)
            {
                return;
            }
        }

        private void HandleMovement()
        {
            var moveDirection = this.GetMoveDir(this.PlayerInput.MoveVector);
            var desiredVelocity = moveDirection * this.PlayerMovementSpeed;
            var currentVelocity = this.PlayerRigidbody.linearVelocity;

            if (this._isGrounded && currentVelocity.y < 0f)
            {
                currentVelocity.y = 0f;
            }

            currentVelocity.x = desiredVelocity.x;
            currentVelocity.z = desiredVelocity.z;
            this.PlayerRigidbody.linearVelocity = currentVelocity;
        }

        private void HandleRotateAim()
        {
            //transform.localEulerAngles = new Vector3(0, this.PlayerCameraController.transform.localEulerAngles.y, 0);
            //this.AimRig.localEulerAngles = new Vector3(this.PlayerCameraController.transform.localEulerAngles.x, 0, 0);

            this._aimYaw += this.PlayerInput.AimVector.x * Time.deltaTime * this.PlayerSensitivity;
            this._aimPitch = Mathf.Clamp(
                this._aimPitch - this.PlayerInput.AimVector.y * Time.deltaTime * this.PlayerSensitivity,
                this.PlayerCameraController.PitchMinLimit,
                this.PlayerCameraController.PitchMaxLimit);
            var rot = Quaternion.Euler(this._aimPitch, this._aimYaw, 0f);

            this.PlayerCameraController.transform.SetPositionAndRotation(this.PlayerCameraController.transform.position, rot);
        }

        private void HandleJump()
        {
            if (!this.isLocalPlayer)
            {
                return;
            }

            if (this.PlayerRigidbody.linearVelocity.y < 0f)
            {
                var vel = this.PlayerRigidbody.linearVelocity;
                vel.y = 0f;
                this.PlayerRigidbody.linearVelocity = vel;
            }
            this.PlayerRigidbody.AddForce(Vector3.up * this.PlayerJumpForce, ForceMode.VelocityChange);
        }

        private Vector3 GetMoveDir(Vector3 inputVector)
        {
            var up = this._groundNormal;
            var aimForwardFlat = Quaternion.Euler(0f, this._aimYaw, 0f) * Vector3.forward;

            var forward = Vector3.ProjectOnPlane(aimForwardFlat, up).normalized;
            var right = Vector3.Cross(up, forward).normalized;

            return Vector3.ClampMagnitude(right * inputVector.x + forward * inputVector.z, 1f);
        }

        private void IsGroundedCheck()
        {
            var groundCheckOrigin = this.PlayerRigidbody.position + this.PlayerRigidbody.rotation * this._groundCheckLocalOffset;
            var groundHit = Physics.SphereCast(groundCheckOrigin,
                this.GroundRadiusCheck, Vector3.down, out var hit, this.GroundCastDistance, this.GroundLayerMask);

            if (groundHit)
            {
                var angle = Vector3.Angle(hit.normal, Vector3.up);
                this._isGrounded = angle <= this.GroundMaxSlopeAngle;
                this._groundNormal = hit.normal;
            }
            else
            {
                this._isGrounded = false;
                this._groundNormal = Vector3.up;
            }
        }

        private void HandleCursorToggle()
        {
            if (Mouse.current == null || !Mouse.current.middleButton.wasPressedThisFrame)
            {
                return;
            }

            this._isCursorLocked = !this._isCursorLocked;
            this.SetCursorState(this._isCursorLocked);
        }

        private void SetCursorState(bool isLocked)
        {
            Cursor.visible = !isLocked;
            Cursor.lockState = isLocked ? CursorLockMode.Locked : CursorLockMode.None;
        }
    }
}
