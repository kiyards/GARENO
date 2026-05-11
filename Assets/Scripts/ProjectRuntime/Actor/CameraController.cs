using Mirror;
using Unity.Cinemachine;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class CameraController : NetworkBehaviour
    {
        [field: SerializeField, Header("Scene References")]
        private CinemachineCamera AimCamera { get; set; }

        [field: SerializeField]
        private PlayerInput PlayerInput { get; set; }

        [field: SerializeField, Header("Player Settings")]
        private float PitchMin { get; set; } = -80f;

        [field: SerializeField]
        private float PitchMax { get; set; } = 80f;

        public float PitchMinLimit => this.PitchMin;
        public float PitchMaxLimit => this.PitchMax;

        public float Yaw { get; private set; }
        public float Pitch { get; private set; }

        private void LateUpdate()
        {
            if (!this.isLocalPlayer)
            {
                return;
            }

            this.HandleCameraAim(this.PlayerInput.AimVector);
        }

        public void HandleCameraAim(Vector2 aimVec)
        {
            this.Yaw += aimVec.x;
            this.Pitch -= aimVec.y;
            this.Pitch = Mathf.Clamp(this.Pitch, this.PitchMin, this.PitchMax);

            this.transform.rotation = Quaternion.Euler(this.Pitch, this.Yaw, 0f);
        }

        public void SetLocalCameraActive(bool isActive)
        {
            this.enabled = isActive;
            this.AimCamera.enabled = isActive;
            this.AimCamera.gameObject.SetActive(isActive);
        }
    }
}
