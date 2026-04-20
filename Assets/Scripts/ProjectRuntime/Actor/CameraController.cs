using Mirror;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class CameraController : NetworkBehaviour
    {
        [field: SerializeField, Header("Scene References")]
        private CinemachineCamera AimCamera { get; set; }

        [field: SerializeField, Header("Player Settings")]
        private float PitchMin { get; set; } = -80f;

        [field: SerializeField]
        private float PitchMax { get; set; } = 80f;

        public float PitchMinLimit => this.PitchMin;
        public float PitchMaxLimit => this.PitchMax;

        private CinemachineFollow _cinemachineFollow;

        private void Awake()
        {
            this._cinemachineFollow = this.AimCamera.GetComponent<CinemachineFollow>();
        }

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();
            this.ConfigureFirstPersonCamera();
        }

        private void ConfigureFirstPersonCamera()
        {
            if (this._cinemachineFollow == null)
            {
                return;
            }

            var trackerSettings = this._cinemachineFollow.TrackerSettings;
            trackerSettings.BindingMode = BindingMode.LockToTarget;
            trackerSettings.PositionDamping = Vector3.zero;
            trackerSettings.RotationDamping = Vector3.zero;
            trackerSettings.QuaternionDamping = 0f;
            this._cinemachineFollow.TrackerSettings = trackerSettings;
            this._cinemachineFollow.FollowOffset = Vector3.zero;
        }
    }
}
