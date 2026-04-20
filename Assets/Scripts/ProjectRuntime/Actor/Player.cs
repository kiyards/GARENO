using Mirror;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class Player : NetworkBehaviour
    {
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

        private void Update()
        {
            this.IsGroundedCheck();
            if (this.PlayerInput.JumpPress && this._isGrounded)
            {
                this.HandleJump();
            }
        }

        private void FixedUpdate()
        {
            this.HandleMovement();
        }

        private void LateUpdate()
        {
            this.HandleRotateAim();
        }

        private void HandleMovement()
        {
            var moveDirection = this.GetMoveDir(Vector3.zero);
            var moveDelta = this.PlayerMovementSpeed * Time.fixedDeltaTime * moveDirection;
            this.PlayerRigidbody.MovePosition(this.transform.position + moveDelta);
        }

        private void HandleRotateAim()
        {
            this.transform.localEulerAngles = new Vector3(0f, this.PlayerCameraController.transform.localEulerAngles.y, 0f);
            this.AimRig.localEulerAngles = new Vector3(this.PlayerCameraController.transform.localEulerAngles.x, 0, 0);
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
            var aim = this.PlayerCameraController.transform;
            var up = this._groundNormal;
            var aimForwardFlat = Quaternion.Euler(0f, aim.eulerAngles.y, 0) * Vector3.forward;

            var forward = Vector3.ProjectOnPlane(aimForwardFlat, up).normalized;
            var right = Vector3.Cross(up, forward).normalized;

            return right * inputVector.x + forward * inputVector.z;
        }

        private void IsGroundedCheck()
        {
            var groundHit = Physics.SphereCast(this.GroundCheckTransform.position,
                this.GroundRadiusCheck, Vector3.down, out var hit, this.GroundCastDistance, this.GroundLayerMask);

            if (groundHit)
            {
                var angle = Vector3.Angle(hit.normal, Vector3.up);
                this._isGrounded = angle <= this.GroundMaxSlopeAngle;
                this._groundNormal = hit.normal;
            }
            else
            {
                this._groundNormal = Vector3.up;
            }
        }
    }
}