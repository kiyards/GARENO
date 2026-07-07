using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectRuntime.Actor
{
    public class DungeonMasterTurretController : MonoBehaviour
    {
        private const double AimSyncInterval = 0.05;
        private const float AimSyncDirectionDotThreshold = 0.999f;
        private const float PlacementSampleRadius = 50f;

        [Header("Aim")]
        [SerializeField]
        private LayerMask aimOcclusionMask;

        [SerializeField]
        private LayerMask aimTargetMask;

        [Header("Feedback")]
        // Matches the survivor pistol's shake feel (PistolWeapon shakeAmplitude/shakeDuration).
        [SerializeField]
        private float shakeAmplitude = 0.8f;

        [SerializeField]
        private float shakeDuration = 0.3f;

        private GameplayPlayer _player;
        private DungeonMasterTurret _activeTurret;
        private double _clientLastFireTime;
        private double _serverLastFireTime;
        private double _clientLastAimSyncTime;
        private Vector3 _lastSyncedAimDirection;

        public float MaxRange => _activeTurret != null ? _activeTurret.MaxRange : 0f;
        public bool IsDisassembling => _activeTurret != null && _activeTurret.IsDisassembling;
        public bool IsAssembling => _activeTurret != null && !_activeTurret.IsAssembled && !_activeTurret.IsDisassembling;

        public void Initialize(GameplayPlayer owner)
        {
            _player = owner;
        }

        public void Enter(Vector3 spawnPosition)
        {
            var player = ResolvePlayer();
            if (player != null && player.isServer)
            {
                GameObject prefab = GameNetworkManager.Instance != null
                    ? GameNetworkManager.Instance.DungeonMasterTurretPrefab
                    : null;
                ServerSpawnTurret(spawnPosition, prefab);
            }

            SetVisible(true);
            UpdateAimFromCursor();
        }

        public void Exit()
        {
            SetVisible(false);

            var player = ResolvePlayer();
            if (player != null && player.isServer)
            {
                ServerDestroyTurret();
            }
        }

        public void SetVisible(bool isVisible)
        {
            if (_activeTurret == null)
            {
                return;
            }

            _activeTurret.SetVisible(isVisible);
        }

        public void UpdateAimFromCamera()
        {
            var player = ResolvePlayer();
            if (player == null)
            {
                return;
            }

            UpdateAim(player.cam.transform.forward);
        }

        public void UpdateAimFromCursor()
        {
            if (CursorPlacementUtility.TryGetCursorRay(out Ray ray))
            {
                UpdateAim(ray.direction);
                return;
            }

            UpdateAimFromCamera();
        }

        public void UpdateAim(Vector3 worldDirection)
        {
            if (_activeTurret == null)
            {
                return;
            }

            _activeTurret.UpdateAim(worldDirection);
            TrySyncAim(worldDirection);
        }

        public void TryFire()
        {
            var player = ResolvePlayer();
            if (
                player == null
                || !player.isLocalPlayer
                || !player.IsDungeonMaster
                || !(player.currentState is DungeonMasterTurretState)
            )
            {
                return;
            }

            if (_activeTurret == null || !_activeTurret.IsAssembled)
            {
                return;
            }

            if (_activeTurret.CurrentAmmo <= 0)
            {
                return;
            }

            if (NetworkTime.time - _clientLastFireTime < _activeTurret.FireCooldown)
            {
                return;
            }

            _clientLastFireTime = NetworkTime.time;

            if (!TryGetCursorAim(out Vector3 hitPoint, out uint targetNetId, out Vector3 fireDirection))
            {
                return;
            }

            player.CmdFireDungeonMasterTurret(targetNetId, hitPoint, fireDirection);
            player.cam.AddShake(shakeAmplitude, shakeDuration);
        }

        [Server]
        public void ServerUpdateAim(Vector3 worldDirection)
        {
            var player = ResolvePlayer();
            if (
                player == null
                || !player.IsDungeonMaster
                || !(player.currentState is DungeonMasterTurretState)
                || _activeTurret == null
            )
            {
                return;
            }

            _activeTurret.ServerUpdateAim(player, worldDirection);
        }

        [Server]
        public void ServerFire(uint targetNetId, Vector3 hitPoint, Vector3 fireDirection)
        {
            var player = ResolvePlayer();
            if (
                player == null
                || !player.IsDungeonMaster
                || !(player.currentState is DungeonMasterTurretState)
            )
            {
                return;
            }

            if (_activeTurret == null || !_activeTurret.IsAssembled)
            {
                return;
            }

            if (NetworkTime.time - _serverLastFireTime < _activeTurret.FireCooldown - 0.05f)
            {
                return;
            }

            if (Vector3.Distance(player.transform.position, hitPoint) > _activeTurret.MaxRange * 1.1f)
            {
                return;
            }

            _serverLastFireTime = NetworkTime.time;
            _activeTurret.ServerConsumeAmmo();

            if (
                targetNetId == 0
                || !NetworkServer.spawned.TryGetValue(
                    targetNetId,
                    out NetworkIdentity targetIdentity
                )
            )
            {
                return;
            }

            var targetManager = targetIdentity.GetComponentInParent<PlayerManager>();
            if (targetManager == null || targetManager.playerRole != PlayerRole.Survivor)
            {
                return;
            }

            var damageable =
                targetIdentity.GetComponentInParent<IDamageable>()
                ?? targetIdentity.GetComponentInChildren<IDamageable>();
            if (damageable == null || !damageable.IsAlive)
            {
                return;
            }

            damageable.ServerTakeDamage(_activeTurret.Damage, player.netId, hitPoint);
            _activeTurret.RpcShowDamageNumber(hitPoint, _activeTurret.Damage);
            _activeTurret.RpcPlayHitVfx(hitPoint, fireDirection);

            if (_activeTurret.SlowOnHit)
            {
                var targetPlayer = targetIdentity.GetComponentInParent<GameplayPlayer>();
                if (targetPlayer == null)
                    targetPlayer = targetIdentity.GetComponentInChildren<GameplayPlayer>();
                if (targetPlayer != null)
                    targetPlayer.ServerApplySlow(_activeTurret.SlowAmount, _activeTurret.SlowDuration);
            }
        }

        private bool TryGetCursorAim(out Vector3 hitPoint, out uint targetNetId, out Vector3 fireDirection)
        {
            hitPoint = Vector3.zero;
            targetNetId = 0;
            fireDirection = Vector3.zero;

            var player = ResolvePlayer();
            if (player == null)
            {
                return false;
            }

            if (!CursorPlacementUtility.TryGetCursorRay(out Ray ray))
            {
                return false;
            }

            fireDirection = ray.direction;

            float range = _activeTurret != null ? _activeTurret.MaxRange : 0f;
            bool hasOcclusion = Physics.Raycast(
                ray,
                out RaycastHit occlusionHit,
                range,
                aimOcclusionMask
            );
            hitPoint = hasOcclusion ? occlusionHit.point : ray.origin + ray.direction * range;

            var hits = Physics.RaycastAll(ray, range, aimTargetMask);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                if (hit.collider == player.col)
                {
                    continue;
                }

                if (IsActiveTurretCollider(hit.collider))
                {
                    continue;
                }

                if (hasOcclusion && hit.distance > occlusionHit.distance)
                {
                    break;
                }

                // The turret should only lock onto survivors — traps, other turrets, the
                // crystal, and zombies all share the Damageable layer but aren't valid targets,
                // so skip past them (they don't block the shot) and keep looking.
                var targetManager = hit.collider.GetComponentInParent<PlayerManager>();
                if (targetManager == null || targetManager.playerRole != PlayerRole.Survivor)
                {
                    continue;
                }

                hitPoint = hit.point;
                var identity = hit.collider.GetComponentInParent<NetworkIdentity>();
                if (identity != null)
                {
                    targetNetId = identity.netId;
                }

                break;
            }

            return true;
        }

        private bool IsActiveTurretCollider(Collider hitCollider)
        {
            return hitCollider != null
                && _activeTurret != null
                && hitCollider.GetComponentInParent<DungeonMasterTurret>() == _activeTurret;
        }

        public void AttachSpawnedTurret(DungeonMasterTurret turret)
        {
            var player = ResolvePlayer();
            if (player == null || turret == null || !turret.IsOwnedBy(player))
            {
                return;
            }

            _activeTurret = turret;
            _clientLastAimSyncTime = 0;
            _lastSyncedAimDirection = Vector3.zero;
            _activeTurret.SetVisible(true);
        }

        public void DetachSpawnedTurret(DungeonMasterTurret turret)
        {
            if (_activeTurret != turret)
            {
                return;
            }

            _activeTurret = null;

            var player = ResolvePlayer();
            if (
                player != null
                && player.isLocalPlayer
                && player.currentState is DungeonMasterTurretState
            )
            {
                player.QueueState(new DungeonMasterMovementState(player));
            }
        }

        public void ServerBeginDisassemble()
        {
            if (_activeTurret == null)
            {
                return;
            }

            _activeTurret.ServerBeginDisassemble();
        }

        [Server]
        public void ServerStartTurretLifetime()
        {
            if (_activeTurret == null)
            {
                return;
            }

            _activeTurret.ServerStartLifetime();
        }

        public void ClientStartTurretLifetime()
        {
            var player = ResolvePlayer();
            if (player == null || !player.isLocalPlayer || _activeTurret == null || !_activeTurret.IsAssembled)
            {
                return;
            }

            player.CmdStartTurretLifetime();
        }

        [Server]
        public bool ServerSpawnTurretForCard(Vector3 position)
        {
            var player = ResolvePlayer();
            if (player == null || !player.IsDungeonMaster || _activeTurret != null)
            {
                return false;
            }

            GameObject prefab = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.DungeonMasterTurretPrefab
                : null;
            return ServerSpawnTurret(position, prefab);
        }

        [Server]
        public bool ServerSpawnSlowingTurretForCard(Vector3 position)
        {
            var player = ResolvePlayer();
            if (player == null || !player.IsDungeonMaster || _activeTurret != null)
            {
                return false;
            }

            GameObject prefab = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.DungeonMasterSlowingTurretPrefab
                : null;
            return ServerSpawnTurret(position, prefab);
        }

        [Server]
        private bool ServerSpawnTurret(Vector3 spawnPosition, GameObject turretPrefab)
        {
            var player = ResolvePlayer();
            if (player == null || !player.IsDungeonMaster || _activeTurret != null)
            {
                return false;
            }

            if (turretPrefab == null)
            {
                Debug.LogWarning(
                    "[DungeonMasterTurretController] Turret prefab is null — assign it on GameNetworkManager."
                );
                return false;
            }

            if (!turretPrefab.TryGetComponent(out DungeonMasterTurret _))
            {
                Debug.LogWarning(
                    "[DungeonMasterTurretController] Turret prefab must have a DungeonMasterTurret component on its root."
                );
                return false;
            }

            if (!NavMesh.SamplePosition(
                    spawnPosition,
                    out NavMeshHit navMeshHit,
                    PlacementSampleRadius,
                    NavMesh.AllAreas))
            {
                return false;
            }

            Vector3 flatForward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
            Quaternion spawnRotation = flatForward.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(flatForward, Vector3.up)
                : Quaternion.identity;
            GameObject turretObject = Instantiate(
                turretPrefab,
                navMeshHit.position,
                spawnRotation
            );
            var turret = turretObject.GetComponent<DungeonMasterTurret>();
            turret.ServerInitialize(player);
            NetworkServer.Spawn(turretObject);
            AttachSpawnedTurret(turret);
            turret.SetVisible(true);
            return true;
        }

        [Server]
        private void ServerDestroyTurret()
        {
            if (_activeTurret == null)
            {
                return;
            }

            DungeonMasterTurret turret = _activeTurret;
            _activeTurret = null;
            NetworkServer.Destroy(turret.gameObject);
        }

        private GameplayPlayer ResolvePlayer()
        {
            if (_player == null)
            {
            _player = GetComponent<GameplayPlayer>();
            }

            return _player;
        }

        private void TrySyncAim(Vector3 worldDirection)
        {
            var player = ResolvePlayer();
            if (
                player == null
                || !player.isLocalPlayer
                || !player.IsDungeonMaster
                || !(player.currentState is DungeonMasterTurretState)
                || _activeTurret == null
                || !_activeTurret.IsAssembled
                || worldDirection.sqrMagnitude <= 0.0001f
            )
            {
                return;
            }

            Vector3 normalizedDirection = worldDirection.normalized;
            bool hasSyncedAim = _lastSyncedAimDirection.sqrMagnitude > 0.0001f;
            bool directionChanged =
                !hasSyncedAim
                || Vector3.Dot(_lastSyncedAimDirection, normalizedDirection)
                    < AimSyncDirectionDotThreshold;
            bool intervalElapsed =
                NetworkTime.time - _clientLastAimSyncTime >= AimSyncInterval;

            if (!directionChanged || (hasSyncedAim && !intervalElapsed))
            {
                return;
            }

            _clientLastAimSyncTime = NetworkTime.time;
            _lastSyncedAimDirection = normalizedDirection;
            player.CmdUpdateDungeonMasterTurretAim(normalizedDirection);
        }
    }
}
