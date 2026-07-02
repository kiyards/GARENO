using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class DungeonMasterTurretController : MonoBehaviour
    {
        [Header("Aim")]
        [SerializeField]
        private LayerMask aimOcclusionMask;

        [SerializeField]
        private LayerMask aimTargetMask;

        private GameplayPlayer _player;
        private DungeonMasterTurret _activeTurret;
        private double _clientLastFireTime;
        private double _serverLastFireTime;

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

            Vector3 aimDirection =
                player.cam != null ? player.cam.transform.forward : player.transform.forward;
            UpdateAim(aimDirection);
        }

        public void UpdateAimFromCursor()
        {
            if (TryGetCursorAim(out Vector3 hitPoint, out _))
            {
                UpdateAim(hitPoint - GetMuzzlePosition());
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

            if (player.cam == null || player.input == null)
            {
                return;
            }

            if (NetworkTime.time - _clientLastFireTime < _activeTurret.FireCooldown)
            {
                return;
            }

            _clientLastFireTime = NetworkTime.time;

            if (!TryGetCursorAim(out Vector3 hitPoint, out uint targetNetId))
            {
                return;
            }

            player.CmdFireDungeonMasterTurret(targetNetId, hitPoint);
        }

        [Server]
        public void ServerFire(uint targetNetId, Vector3 hitPoint)
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

            if (_activeTurret.SlowOnHit)
            {
                var targetPlayer = targetIdentity.GetComponentInParent<GameplayPlayer>();
                if (targetPlayer == null)
                    targetPlayer = targetIdentity.GetComponentInChildren<GameplayPlayer>();
                if (targetPlayer != null)
                    targetPlayer.ServerApplySlow(_activeTurret.SlowAmount, _activeTurret.SlowDuration);
            }
        }

        private Vector3 GetMuzzlePosition()
        {
            if (_activeTurret != null)
            {
                return _activeTurret.GetMuzzlePosition();
            }

            var player = ResolvePlayer();
            return player != null
                ? player.transform.position + player.transform.forward * 0.75f
                : transform.position;
        }

        private bool TryGetCursorAim(out Vector3 hitPoint, out uint targetNetId)
        {
            hitPoint = Vector3.zero;
            targetNetId = 0;

            var player = ResolvePlayer();
            if (player == null || player.cam == null)
            {
                return false;
            }

            if (!CursorPlacementUtility.TryGetCursorRay(out Ray ray))
            {
                return false;
            }

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

                if (hasOcclusion && hit.distance > occlusionHit.distance)
                {
                    break;
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

        public void AttachSpawnedTurret(DungeonMasterTurret turret)
        {
            var player = ResolvePlayer();
            if (player == null || turret == null || !turret.IsOwnedBy(player))
            {
                return;
            }

            _activeTurret = turret;
            _activeTurret.SetVisible(player.currentState is DungeonMasterTurretState);
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
            ServerSpawnTurret(position, prefab);
            return true;
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
            ServerSpawnTurret(position, prefab);
            return true;
        }

        [Server]
        private void ServerSpawnTurret(Vector3 spawnPosition, GameObject turretPrefab)
        {
            var player = ResolvePlayer();
            if (player == null || !player.IsDungeonMaster || _activeTurret != null)
            {
                return;
            }

            if (turretPrefab == null)
            {
                Debug.LogWarning(
                    "[DungeonMasterTurretController] Turret prefab is null — assign it on GameNetworkManager."
                );
                return;
            }

            if (!turretPrefab.TryGetComponent(out DungeonMasterTurret _))
            {
                Debug.LogWarning(
                    "[DungeonMasterTurretController] Turret prefab must have a DungeonMasterTurret component on its root."
                );
                return;
            }

            float heightOffset = 1f;
            GameObject turretObject = Instantiate(
                turretPrefab,
                spawnPosition + Vector3.up * heightOffset,
                Quaternion.identity
            );
            var turret = turretObject.GetComponent<DungeonMasterTurret>();
            turret.ServerInitialize(player);
            NetworkServer.Spawn(turretObject);
            AttachSpawnedTurret(turret);
            turret.SetVisible(true);
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
    }
}
