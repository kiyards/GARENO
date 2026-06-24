using Mirror;
using ProjectRuntime.Actor;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class CameraController : NetworkBehaviour
    {
        public CinemachineThirdPersonAim thirdPersonAim;
        public CinemachineCamera thirdPersonCam;
        public CinemachineCamera firstPersonCam;

        public LayerMask aimOcclusionMask;
        public LayerMask aimTargetMask;

        public GameplayPlayer player;
        public PlayerInput input;
        public float sens;

        [HideInInspector] public Transform spectateTarget;

        [SyncVar] float _pitch;
        [SyncVar] float _yaw;
        [SyncVar] CharacterMode characterMode;

        [SerializeField] float pitchMin = -80f;
        [SerializeField] float pitchMax = 80f;
        [SerializeField] float aimFirepointOffset = 0.1f;

        public Vector3 GetAimOrigin() => firstPersonCam.transform.position + firstPersonCam.transform.forward * aimFirepointOffset;

        public override void OnStartClient()
        {
            base.OnStartClient();
            thirdPersonCam.Priority.Enabled = isLocalPlayer;
            firstPersonCam.Priority.Enabled = isLocalPlayer;
        }
        private void Update()
        {
            if (!isLocalPlayer) return;
            ControlCam(input.aimVec);
        }
        public void ControlCam(Vector2 aimVec)
        {
            _yaw += aimVec.x * Time.deltaTime;
            _pitch -= aimVec.y * Time.deltaTime;
            _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

            var pos = spectateTarget != null ? spectateTarget.position : player.transform.position;
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);

            transform.SetPositionAndRotation(pos, rot);
        }
        public void SetSpectateTarget(Transform target) => spectateTarget = target;
        public void ClearSpectateTarget() => spectateTarget = null;
        public void SetCam(CharacterMode mode)
        {
            if (!isLocalPlayer) return;
            switch (mode)
            {
                case CharacterMode.SHOULDER:
                case CharacterMode.SPECTATE:
                    thirdPersonCam.Priority = 1;
                    firstPersonCam.Priority = 0;
                    break;
                case CharacterMode.AIM:
                    thirdPersonCam.Priority = 0;
                    firstPersonCam.Priority = 1;
                    break;
            }
            characterMode = mode;
        }
        public List<RaycastData> GetRaycastData(float maxRange)
        {
            bool blocked = GetAimData(maxRange, out Vector3 source, out Vector3 dir, out RaycastHit occlusionHit);

            var hits = Physics.RaycastAll(source, dir, maxRange, aimTargetMask);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            var results = new List<RaycastData>();
            foreach (var hit in hits)
            {
                if (hit.collider == player.col) continue;
                if (blocked && hit.distance > occlusionHit.distance) break;
                results.Add(new RaycastData
                {
                    origin = source,
                    direction = dir,
                    hitPoint = hit.point,
                    hit = hit,
                    layerMask = aimTargetMask
                });
            }
            return results;
        }
        public bool GetAimData(float maxRange, out Vector3 origin, out Vector3 dir, out RaycastHit occlusionHit) // returns whether occluded
        {
            origin = firstPersonCam.transform.position;
            dir = characterMode == CharacterMode.AIM
                ? firstPersonCam.transform.forward
                : (thirdPersonAim.AimTarget - origin).normalized;
            return Physics.Raycast(origin, dir, out occlusionHit, maxRange, aimOcclusionMask);
        }
    }
    public struct RaycastData
    {
        public Vector3 origin;
        public Vector3 direction;
        public Vector3 hitPoint;
        public RaycastHit hit; // Not serializable, this should only be used on clients
        public LayerMask layerMask;
        public uint sourceNetId;
    }
}