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
        private LayerMask GroundLayerMask { get; set; }

        [field: SerializeField]
        private float GroundCastDistance { get; set; }

        [field: SerializeField, Header("Player Settings")]
        private float PlayerMovementSpeed { get; set; }

        [field: SerializeField]
        private float PlayerJumpForce { get; set; }

        [field: SerializeField]
        public float PlayerSensitivity { get; set; } // public to get from PlayerInput

        // Internal Variables
        private Vector3 _groundNormal;
        private bool _isGrounded;
        private const float GroundProbePadding = 0.05f;

        // Networking
        private string _playerName;
        private ulong _playerSteamId;
        private int _playerIndex;
        private bool _isCursorLocked = true;

        private void Awake()
        {
            this.PlayerRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            this.PlayerRigidbody.constraints |= RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            this.ApplyLocalControlState(this.isOwned);
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

        public override void OnStartAuthority()
        {
            base.OnStartAuthority();
            this.ApplyLocalControlState(true);
            this.SetCursorState(this._isCursorLocked);
        }

        public override void OnStopAuthority()
        {
            base.OnStopAuthority();
            this.ApplyLocalControlState(false);
            this.SetCursorState(false);
        }

        public void Init(string playerName,  ulong playerSteamId, int playerIndex)
        {
            this._playerName = playerName;
            this._playerSteamId = playerSteamId;
            this._playerIndex = playerIndex;
        }

        private void Update()
        {
            if (!this.isOwned)
            {
                return;
            }

            this.HandleCursorToggle();
            this.IsGroundedCheck();
            if (this.PlayerInput.JumpPress && this._isGrounded)
            {
                this.HandleJump();
            }
        }

        private void FixedUpdate()
        {
            if (!this.isOwned)
            {
                return;
            }

            this.HandleMovement(this.PlayerInput.MoveVector);
        }

        private void HandleMovement(Vector3 inputVector)
        {
            var moveDirection = this.GetMoveDir(inputVector);
            var moveDelta = this.PlayerMovementSpeed * Time.fixedDeltaTime * moveDirection;
            this.PlayerRigidbody.MovePosition(this.PlayerRigidbody.position + moveDelta);
        }

        private void HandleJump()
        {
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
            var aimForwardFlat = Quaternion.Euler(0f, this.PlayerCameraController.transform.eulerAngles.y, 0f) * Vector3.forward;

            var forward = Vector3.ProjectOnPlane(aimForwardFlat, up).normalized;
            var right = Vector3.Cross(up, forward).normalized;

            return right * inputVector.x + forward * inputVector.z;
        }

        private void IsGroundedCheck()
        {
            var colliderBounds = this.PlayerCollider.bounds;
            var castRadius = Mathf.Max(0.05f, Mathf.Min(colliderBounds.extents.x, colliderBounds.extents.z) * 0.9f);
            var castDistance = Mathf.Max(this.GroundCastDistance, GroundProbePadding * 2f);
            var groundCheckOrigin = new Vector3(
                colliderBounds.center.x,
                colliderBounds.min.y + castRadius + GroundProbePadding,
                colliderBounds.center.z);
            var groundHit = Physics.SphereCast(
                groundCheckOrigin,
                castRadius,
                Vector3.down,
                out var hit,
                castDistance,
                this.GroundLayerMask,
                QueryTriggerInteraction.Ignore);

            if (groundHit)
            {
                this._isGrounded = true;
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

        private void ApplyLocalControlState(bool isOwnedByLocalClient)
        {
            this.PlayerInput.enabled = isOwnedByLocalClient;
            this.PlayerCameraController.SetLocalCameraActive(isOwnedByLocalClient);

            if (!isOwnedByLocalClient)
            {
                this.SetCursorState(false);
            }
        }
    }
}
