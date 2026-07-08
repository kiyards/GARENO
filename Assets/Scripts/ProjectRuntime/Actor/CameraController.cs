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
        [SerializeField] private GameObject weaponViewmodel;
        [SerializeField] private GameObject muzzle;

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
        [SerializeField] float maxShakeAmplitude = 2f;
        [SerializeField] float aimFirepointOffset = 0.1f;
        [SerializeField] float topDownHeight = 24f;
        [SerializeField] float topDownPitch = 90f;
        [SerializeField] float topDownYaw = 0f;
        [SerializeField] Collider dungeonMasterBoundingVolume;
        [SerializeField] float dungeonMasterConfinerSlowingDistance = 0f;

        private CinemachineConfiner3D _dungeonMasterConfiner;

        // Local-only camera shake (fire feedback), driven through Cinemachine's Noise stage
        // (CinemachineBasicMultiChannelPerlin on firstPersonCam). Because the Perlin noise mutates
        // the CameraState rather than the vcam transform, it never perturbs the aim raycast
        // (which reads firstPersonCam.transform.forward) — the shake stays purely cosmetic.
        // Trauma model: each shot bumps AmplitudeGain, which decays continuously, so a held trigger
        // reads as repeated settling kicks rather than one sustained buzz. The X/Y-only feel comes
        // from the noise profile (FireShakeNoise) having no roll or position channels.
        private CinemachineBasicMultiChannelPerlin _fireShakeNoise;
        private float _shakeTrauma;
        private float _shakeDecayRate;

        public Vector3 GetAimOrigin() => firstPersonCam.transform.position + firstPersonCam.transform.forward * aimFirepointOffset;

        // Visual-only origin for muzzle VFX (tracers, muzzle flashes). Deliberately offset from the
        // camera so a shot fired dead-center doesn't produce a tracer collinear with the viewer's own
        // optical axis — a LineRenderer segment aligned with the camera's view direction renders with
        // zero apparent width (its camera-facing billboard collapses), making it invisible while stationary.
        public Vector3 GetWeaponMuzzlePosition() =>
            muzzle != null ? muzzle.transform.position
                : weaponViewmodel != null ? weaponViewmodel.transform.position
                : GetAimOrigin();

        public override void OnStartClient()
        {
            base.OnStartClient();
            thirdPersonCam.Priority.Enabled = isLocalPlayer;
            firstPersonCam.Priority.Enabled = isLocalPlayer;

            // Only the owning client should ever see their own viewmodel — remote observers would
            // otherwise see it as a floating gun glued to this player's head (SetCam's AIM/TOP_DOWN
            // toggle only runs for isLocalPlayer, so it never corrects this for anyone else).
            if (weaponViewmodel != null)
            {
                weaponViewmodel.SetActive(isLocalPlayer);
            }

            if (isLocalPlayer)
            {
                firstPersonCam.TryGetComponent(out _fireShakeNoise);
            }
        }

        // Ghosts (permanently dead survivors) live on the "Ghost" physics layer so they pass through
        // players and shots. The rendering camera must still be allowed to render that layer — who
        // actually sees a ghost is gated per-viewer by GameplayPlayer.RefreshGhostVisibility toggling
        // renderers. Enforced in code so it never depends on the scene camera's serialized culling mask.
        private static void EnsureGhostLayerRendered()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            int ghostLayer = LayerMask.NameToLayer("Ghost");
            if (ghostLayer >= 0)
            {
                cam.cullingMask |= 1 << ghostLayer;
            }
        }
        private void Update()
        {
            if (!isLocalPlayer) return;

            UpdateFireShake();

            if (characterMode == CharacterMode.TOP_DOWN)
            {
                ControlTopDownCamera();
                return;
            }

            if (player != null && player.Turret.IsDisassembling)
            {
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

        // Add one kick's worth of trauma. `duration` is how long a lone kick of this amplitude takes
        // to decay to zero, so held fire adds bumps that settle between shots instead of pinning the
        // shake at full strength. Local-only feedback — no networking.
        public void AddShake(float amplitude, float duration)
        {
            _shakeTrauma = Mathf.Min(maxShakeAmplitude, _shakeTrauma + amplitude);
            _shakeDecayRate = amplitude / Mathf.Max(0.0001f, duration);
        }

        // Decay the current trauma and feed it to the Cinemachine noise as AmplitudeGain. The noise
        // profile supplies the frequency and X/Y-only shape; trauma just scales its intensity.
        private void UpdateFireShake()
        {
            if (_fireShakeNoise == null)
            {
                return;
            }

            if (_shakeTrauma > 0f)
            {
                _shakeTrauma = Mathf.Max(0f, _shakeTrauma - _shakeDecayRate * Time.deltaTime);
            }

            _fireShakeNoise.AmplitudeGain = _shakeTrauma;
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

        public void LookTowards(Vector3 worldPosition)
        {
            if (!isLocalPlayer) return;
            Vector3 origin = player != null ? player.transform.position : transform.position;
            Vector3 dir = (worldPosition - origin).normalized;
            if (dir.sqrMagnitude < 0.001f) return;
            _yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            _pitch = Mathf.Clamp(-Mathf.Asin(dir.y) * Mathf.Rad2Deg, pitchMin, pitchMax);
        }

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

            // Only a survivor actively aiming their own pistol should see the viewmodel —
            // Dungeon Master top-down and turret-aim both reuse firstPersonCam without a pistol.
            if (weaponViewmodel != null)
            {
                weaponViewmodel.SetActive(mode == CharacterMode.AIM && player != null && !player.IsDungeonMaster);
            }

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
                // Ghosts (permanently dead survivors) never block or absorb shots — pass through them.
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

            // Nearest occluder, skipping ghosts so they can't cap line-of-sight. Identical to a single
            // nearest-hit raycast when no ghost is in the way.
            occlusionHit = default;
            var occluders = Physics.RaycastAll(origin, dir, maxRange, aimOcclusionMask);
            Array.Sort(occluders, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in occluders)
            {
                occlusionHit = hit;
                return true;
            }

            return false;
        }

        // Ghosts (permanently dead survivors) must never block or absorb shots — shared by the survivor
        // pistol (above) and the Dungeon Master turret.
        private static bool IsGhostCollider(Collider hitCollider)
        {
            if (hitCollider == null)
            {
                return false;
            }

            return false;
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
