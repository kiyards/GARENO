using Unity.Cinemachine;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class CameraController : MonoBehaviour
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

        private void Update()
        {
            if (!this.enabled)
            {
                return;
            }

            this.HandleCameraAim(this.PlayerInput.AimVector);
        }

        public void HandleCameraAim(Vector2 aimVec)
        {
            this.Yaw += aimVec.x * Time.deltaTime;
            this.Pitch -= aimVec.y * Time.deltaTime;
            this.Pitch = Mathf.Clamp(this.Pitch, this.PitchMin, this.PitchMax);

            var cameraRotation = Quaternion.Euler(this.Pitch, this.Yaw, 0f);

            this.transform.SetPositionAndRotation(this.transform.position, cameraRotation);
        }

        public void SetLocalCameraActive(bool isActive)
        {
            this.enabled = isActive;
            this.AimCamera.enabled = isActive;
            this.AimCamera.gameObject.SetActive(isActive);
        }
    }
}
