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
        [SerializeField] float topDownHeight = 24f;
        [SerializeField] float topDownPitch = 90f;
        [SerializeField] float topDownYaw = 0f;
        [SerializeField] Collider dungeonMasterBoundingVolume;
        [SerializeField] float dungeonMasterConfinerSlowingDistance = 0f;

        private CinemachineConfiner3D _dungeonMasterConfiner;

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

            if (characterMode == CharacterMode.TOP_DOWN)
            {
                ControlTopDownCamera();
                return;
            }

            ControlCam(input.aimVec);
        }
        public void ControlCam(Vector2 aimVec)
        {
            if (characterMode == CharacterMode.TOP_DOWN)
            {
                ControlTopDownCamera();
                return;
            }

            _yaw += aimVec.x * Time.deltaTime;
            _pitch -= aimVec.y * Time.deltaTime;
            _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

            var pos = spectateTarget != null ? spectateTarget.position : player.transform.position;
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);

            transform.SetPositionAndRotation(pos, rot);
        }
        public void ControlTopDownCamera()
        {
            if (player == null)
            {
                return;
            }

            _pitch = topDownPitch;
            _yaw = topDownYaw;

            var pos = player.transform.position + Vector3.up * topDownHeight;
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
                    ConfigureDungeonMasterConfiner(false);
                    thirdPersonCam.Priority = 1;
                    firstPersonCam.Priority = 0;
                    break;
                case CharacterMode.AIM:
                    ConfigureDungeonMasterConfiner(false);
                    thirdPersonCam.Priority = 0;
                    firstPersonCam.Priority = 1;
                    break;
                case CharacterMode.TOP_DOWN:
                    thirdPersonCam.Priority = 0;
                    firstPersonCam.Priority = 1;
                    ConfigureDungeonMasterConfiner(true);
                    break;
            }
            characterMode = mode;

            if (mode == CharacterMode.TOP_DOWN)
            {
                ControlTopDownCamera();
            }
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
            origin = firstPersonCam != null ? firstPersonCam.transform.position : transform.position;
            dir = characterMode == CharacterMode.AIM || characterMode == CharacterMode.TOP_DOWN || thirdPersonAim == null
                ? (firstPersonCam != null ? firstPersonCam.transform.forward : transform.forward)
                : (thirdPersonAim.AimTarget - origin).normalized;
            return Physics.Raycast(origin, dir, out occlusionHit, maxRange, aimOcclusionMask);
        }

        private void ConfigureDungeonMasterConfiner(bool isEnabled)
        {
            if (!isEnabled)
            {
                if (_dungeonMasterConfiner != null)
                {
                    _dungeonMasterConfiner.enabled = false;
                }

                return;
            }

            if (firstPersonCam == null)
            {
                return;
            }

            var boundingVolume = ResolveDungeonMasterBoundingVolume();
            if (boundingVolume == null)
            {
                if (_dungeonMasterConfiner != null)
                {
                    _dungeonMasterConfiner.enabled = false;
                }

                return;
            }

            if (_dungeonMasterConfiner == null &&
                !firstPersonCam.TryGetComponent(out _dungeonMasterConfiner))
            {
                _dungeonMasterConfiner = firstPersonCam.gameObject.AddComponent<CinemachineConfiner3D>();
            }

            _dungeonMasterConfiner.BoundingVolume = boundingVolume;
            _dungeonMasterConfiner.SlowingDistance = Mathf.Max(0f, dungeonMasterConfinerSlowingDistance);
            _dungeonMasterConfiner.enabled = true;
        }

        private Collider ResolveDungeonMasterBoundingVolume()
        {
            if (IsValidDungeonMasterBoundingVolume(dungeonMasterBoundingVolume))
            {
                return dungeonMasterBoundingVolume;
            }

            return DungeonMasterCameraBounds.FindBoundingVolume();
        }

        private static bool IsValidDungeonMasterBoundingVolume(Collider candidate)
        {
            return candidate != null &&
                candidate.enabled &&
                candidate.gameObject.activeInHierarchy;
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

    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class DungeonMasterCameraBounds : MonoBehaviour
    {
        [SerializeField] private Collider boundingVolume;

        public Collider BoundingVolume
        {
            get
            {
                if (boundingVolume == null)
                {
                    boundingVolume = GetComponent<Collider>();
                }

                return boundingVolume;
            }
        }

        public static Collider ActiveBoundingVolume { get; private set; }

        public static Collider FindBoundingVolume()
        {
            if (IsValid(ActiveBoundingVolume))
            {
                return ActiveBoundingVolume;
            }

            var bounds = FindFirstObjectByType<DungeonMasterCameraBounds>();
            if (bounds == null)
            {
                return null;
            }

            bounds.Register();
            return bounds.BoundingVolume;
        }

        private void Awake()
        {
            Register();
        }

        private void OnEnable()
        {
            Register();
        }

        private void OnDisable()
        {
            if (ActiveBoundingVolume == BoundingVolume)
            {
                ActiveBoundingVolume = null;
            }
        }

        private void Register()
        {
            if (IsValid(BoundingVolume))
            {
                ActiveBoundingVolume = BoundingVolume;
            }
        }

        private static bool IsValid(Collider candidate)
        {
            return candidate != null &&
                candidate.enabled &&
                candidate.gameObject.activeInHierarchy;
        }
    }
}
