using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class DungeonMasterTurretController : MonoBehaviour
    {
        [Header("Visuals")]
        [SerializeField] private Transform turretRoot;
        [SerializeField] private Transform turretYawPivot;
        [SerializeField] private Transform turretPitchPivot;
        [SerializeField] private Transform turretMuzzle;

        [Header("Combat")]
        [SerializeField] private float damage = 20f;
        [SerializeField] private float maxRange = 80f;
        [SerializeField] private float fireCooldown = 0.18f;

        [Header("Tracer")]
        [SerializeField] private float tracerDuration = 0.08f;
        [SerializeField] private float tracerWidth = 0.035f;
        [SerializeField] private Color tracerColor = new(1f, 0.76f, 0.22f, 1f);
        [SerializeField] private Material tracerMaterial;

        private GameplayPlayer _player;
        private bool _visualCached;
        private double _clientLastFireTime;
        private double _serverLastFireTime;

        public float MaxRange => maxRange;

        public void Initialize(GameplayPlayer owner)
        {
            _player = owner;
            SetVisible(false);
        }

        public void Enter()
        {
            SetVisible(true);
            UpdateAimFromCamera();
        }

        public void Exit()
        {
            SetVisible(false);
        }

        public void SetVisible(bool isVisible)
        {
            EnsureVisual();

            if (turretRoot == null)
            {
                return;
            }

            turretRoot.gameObject.SetActive(isVisible);

            foreach (Renderer turretRenderer in turretRoot.GetComponentsInChildren<Renderer>(true))
            {
                turretRenderer.enabled = isVisible;
            }
        }

        public void UpdateAimFromCamera()
        {
            var player = ResolvePlayer();
            if (player == null)
            {
                return;
            }

            Vector3 aimDirection = player.cam != null
                ? player.cam.transform.forward
                : player.transform.forward;
            UpdateAim(aimDirection);
        }

        public void UpdateAim(Vector3 worldDirection)
        {
            EnsureVisual();

            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 flatDirection = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
            if (turretYawPivot != null && flatDirection.sqrMagnitude > 0.0001f)
            {
                turretYawPivot.rotation = Quaternion.LookRotation(flatDirection.normalized, Vector3.up);
            }

            if (turretPitchPivot != null)
            {
                turretPitchPivot.rotation = Quaternion.LookRotation(worldDirection.normalized, Vector3.up);
            }
        }

        public void TryFire()
        {
            var player = ResolvePlayer();
            if (player == null ||
                !player.isLocalPlayer ||
                !player.IsDungeonMaster ||
                !(player.currentState is DungeonMasterTurretState))
            {
                return;
            }

            if (player.cam == null || player.input == null)
            {
                return;
            }

            if (NetworkTime.time - _clientLastFireTime < fireCooldown)
            {
                return;
            }

            _clientLastFireTime = NetworkTime.time;

            player.cam.GetAimData(maxRange, out Vector3 origin, out Vector3 direction, out RaycastHit occlusionHit);
            Vector3 hitPoint = occlusionHit.collider != null
                ? occlusionHit.point
                : origin + direction * maxRange;
            uint targetNetId = 0;

            var hits = player.cam.GetRaycastData(maxRange);
            if (hits.Count > 0)
            {
                var first = hits[0];
                hitPoint = first.hitPoint;
                var identity = first.hit.collider.GetComponentInParent<NetworkIdentity>();
                if (identity != null)
                {
                    targetNetId = identity.netId;
                }
            }

            player.CmdFireDungeonMasterTurret(targetNetId, hitPoint);
        }

        [Server]
        public void ServerFire(uint targetNetId, Vector3 hitPoint)
        {
            var player = ResolvePlayer();
            if (player == null ||
                !player.IsDungeonMaster ||
                !(player.currentState is DungeonMasterTurretState))
            {
                return;
            }

            if (NetworkTime.time - _serverLastFireTime < fireCooldown - 0.05f)
            {
                return;
            }

            if (Vector3.Distance(player.transform.position, hitPoint) > maxRange * 1.1f)
            {
                return;
            }

            _serverLastFireTime = NetworkTime.time;
            player.RpcShowDungeonMasterTurretTracer(hitPoint);

            if (targetNetId == 0 ||
                !NetworkServer.spawned.TryGetValue(targetNetId, out NetworkIdentity targetIdentity))
            {
                return;
            }

            var targetManager = targetIdentity.GetComponentInParent<PlayerManager>();
            if (targetManager == null || targetManager.playerRole != PlayerRole.Survivor)
            {
                return;
            }

            var damageable = targetIdentity.GetComponentInParent<IDamageable>() ??
                targetIdentity.GetComponentInChildren<IDamageable>();
            if (damageable == null || !damageable.IsAlive)
            {
                return;
            }

            damageable.ServerTakeDamage(damage, player.netId, hitPoint);
        }

        public void ShowTracer(Vector3 hitPoint)
        {
            Vector3 muzzlePosition = GetMuzzlePosition();
            UpdateAim(hitPoint - muzzlePosition);
            BulletTracer.Spawn(this, muzzlePosition, hitPoint, tracerDuration, tracerWidth,
                tracerColor, tracerMaterial, "DungeonMasterTurretTracer");
        }

        private Vector3 GetMuzzlePosition()
        {
            EnsureVisual();

            if (turretMuzzle != null)
            {
                return turretMuzzle.position;
            }

            var player = ResolvePlayer();
            return player != null
                ? player.transform.position + player.transform.forward * 0.75f
                : transform.position;
        }

        private void EnsureVisual()
        {
            if (_visualCached)
            {
                return;
            }

            _visualCached = true;

            if (turretRoot != null)
            {
                turretRoot.gameObject.SetActive(false);
                return;
            }

            turretRoot = new GameObject("DungeonMasterTurretRoot").transform;
            turretRoot.SetParent(transform, false);
            turretRoot.localPosition = Vector3.zero;
            turretRoot.localRotation = Quaternion.identity;

            turretYawPivot = new GameObject("YawPivot").transform;
            turretYawPivot.SetParent(turretRoot, false);
            turretYawPivot.localPosition = Vector3.zero;
            turretYawPivot.localRotation = Quaternion.identity;

            turretPitchPivot = new GameObject("PitchPivot").transform;
            turretPitchPivot.SetParent(turretYawPivot, false);
            turretPitchPivot.localPosition = Vector3.up * 0.45f;
            turretPitchPivot.localRotation = Quaternion.identity;

            var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "TurretBase";
            baseObject.transform.SetParent(turretRoot, false);
            baseObject.transform.localPosition = Vector3.up * 0.15f;
            baseObject.transform.localScale = new Vector3(0.8f, 0.15f, 0.8f);
            RemoveGeneratedCollider(baseObject);

            var barrelObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrelObject.name = "TurretBarrel";
            barrelObject.transform.SetParent(turretPitchPivot, false);
            barrelObject.transform.localPosition = new Vector3(0f, 0f, 0.55f);
            barrelObject.transform.localScale = new Vector3(0.22f, 0.22f, 1.1f);
            RemoveGeneratedCollider(barrelObject);

            turretMuzzle = new GameObject("MuzzlePoint").transform;
            turretMuzzle.SetParent(turretPitchPivot, false);
            turretMuzzle.localPosition = new Vector3(0f, 0f, 1.15f);
            turretMuzzle.localRotation = Quaternion.identity;

            turretRoot.gameObject.SetActive(false);
        }

        private GameplayPlayer ResolvePlayer()
        {
            if (_player == null)
            {
                _player = GetComponent<GameplayPlayer>();
            }

            return _player;
        }

        private static void RemoveGeneratedCollider(GameObject generatedObject)
        {
            if (generatedObject.TryGetComponent(out Collider generatedCollider))
            {
                Destroy(generatedCollider);
            }
        }
    }
}
