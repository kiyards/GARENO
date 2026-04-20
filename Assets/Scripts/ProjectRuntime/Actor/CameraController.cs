using Mirror;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

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

        private void Update()
        {
            if (!this.isLocalPlayer)
            {
                return;
            }

            this.ControlCamera(this.PlayerInput);
        }

        private void ControlCamera(PlayerInput playerInput)
        {

        }
    }
}