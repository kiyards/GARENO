using Mirror;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public enum TrapType
    {
        BearTrap,
        C4,
        Flashbang,
    }

    public class DungeonMasterTrapController : MonoBehaviour
    {
        [Header("Placement")]
        [SerializeField] private float maxPlacementRange = 80f;
        [SerializeField] private LayerMask placementSurfaceMask = Physics.DefaultRaycastLayers;
        [SerializeField] private float placementCooldown = 0.35f;
        [SerializeField] private float groundProbeHeight = 3f;
        [SerializeField] private float maxGroundSnapDistance = 2f;
        [SerializeField] private float minGroundNormalY = 0.65f;
        [SerializeField] private float trapOverlapRadius = 1.25f;

        private GameplayPlayer _player;
        private double _clientLastPlaceTime;
        private double _serverLastPlaceTime;

        public void Initialize(GameplayPlayer owner)
        {
            _player = owner;
        }

        public void TryPlace(TrapType trapType)
        {
            var player = ResolvePlayer();
            if (player == null ||
                !player.isLocalPlayer ||
                !player.IsDungeonMaster ||
                !(player.currentState is PlayerStates.DungeonMasterMovementState))
            {
                return;
            }

            if (NetworkTime.time - _clientLastPlaceTime < placementCooldown)
            {
                return;
            }

            if (!CursorPlacementUtility.TryGetPlacementFromCursor(
                    maxPlacementRange,
                    placementSurfaceMask,
                    out Vector3 position,
                    out Vector3 normal))
            {
                return;
            }

            _clientLastPlaceTime = NetworkTime.time;
            player.CmdPlaceTrap(trapType, position, normal);
        }

        [Server]
        public void ServerPlace(TrapType trapType, Vector3 requestedPosition, Vector3 requestedNormal)
        {
            var player = ResolvePlayer();
            if (player == null ||
                !player.IsDungeonMaster ||
                !(player.currentState is PlayerStates.DungeonMasterMovementState))
            {
                return;
            }

            if (NetworkTime.time - _serverLastPlaceTime < placementCooldown - 0.05f)
            {
                return;
            }

            if (!TryValidatePlacement(requestedPosition, requestedNormal, out Vector3 position, out Vector3 normal))
            {
                return;
            }

            _serverLastPlaceTime = NetworkTime.time;
            ServerSpawnTrap(trapType, position, normal);
        }

        [Server]
        public bool ServerPlaceFromCard(TrapType trapType, Vector3 requestedPosition, Vector3 requestedNormal)
        {
            var player = ResolvePlayer();
            if (player == null || !player.IsDungeonMaster)
            {
                return false;
            }

            if (!TryValidatePlacement(requestedPosition, requestedNormal, out Vector3 position, out Vector3 normal))
            {
                return false;
            }

            ServerSpawnTrap(trapType, position, normal);
            return true;
        }

        [Server]
        private void ServerSpawnTrap(TrapType trapType, Vector3 position, Vector3 normal)
        {
            if (GameNetworkManager.Instance == null)
            {
                Debug.LogWarning("[DungeonMasterTrapController] GameNetworkManager not found.");
                return;
            }

            GameObject trapPrefab = trapType switch
            {
                TrapType.BearTrap  => GameNetworkManager.Instance.BearTrapPrefab,
                TrapType.C4        => GameNetworkManager.Instance.C4TrapPrefab,
                TrapType.Flashbang => GameNetworkManager.Instance.FlashbangPrefab,
                _                  => null,
            };

            if (trapPrefab == null)
            {
                Debug.LogWarning($"[DungeonMasterTrapController] Prefab for {trapType} is not assigned on GameNetworkManager.");
                return;
            }

            if (trapType == TrapType.BearTrap && !trapPrefab.TryGetComponent(out BearTrap _))
            {
                Debug.LogWarning("[DungeonMasterTrapController] Bear trap prefab must have a BearTrap component on its root.");
                return;
            }

            if (trapType == TrapType.C4 && !trapPrefab.TryGetComponent(out C4Trap _))
            {
                Debug.LogWarning("[DungeonMasterTrapController] C4 prefab must have a C4Trap component on its root.");
                return;
            }

            if (trapType == TrapType.Flashbang && !trapPrefab.TryGetComponent(out FlashbangTrap _))
            {
                Debug.LogWarning("[DungeonMasterTrapController] Flashbang prefab must have a FlashbangTrap component on its root.");
                return;
            }

            // Flashbang spawns elevated so gravity brings it down and it bounces before flashing.
            Vector3 spawnPos = trapType == TrapType.Flashbang ? position + Vector3.up * 2.5f : position;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
            GameObject trapObject = Instantiate(trapPrefab, spawnPos, rotation);
            NetworkServer.Spawn(trapObject);
        }

        private bool TryValidatePlacement(
            Vector3 requestedPosition,
            Vector3 requestedNormal,
            out Vector3 position,
            out Vector3 normal)
        {
            position = Vector3.zero;
            normal = Vector3.up;

            var player = ResolvePlayer();
            if (player == null)
            {
                return false;
            }

            if (Vector3.Distance(player.transform.position, requestedPosition) > maxPlacementRange * 1.15f)
            {
                return false;
            }

            Vector3 probeStart = requestedPosition + Vector3.up * groundProbeHeight;
            if (!Physics.Raycast(probeStart, Vector3.down, out RaycastHit groundHit,
                    groundProbeHeight * 2f, placementSurfaceMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (Vector3.Distance(groundHit.point, requestedPosition) > maxGroundSnapDistance)
            {
                return false;
            }

            if (groundHit.normal.y < minGroundNormalY)
            {
                return false;
            }

            if (requestedNormal.sqrMagnitude > 0.0001f &&
                Vector3.Dot(requestedNormal.normalized, groundHit.normal) < 0.5f)
            {
                return false;
            }

            foreach (Collider nearbyCollider in Physics.OverlapSphere(
                         groundHit.point,
                         trapOverlapRadius,
                         Physics.AllLayers,
                         QueryTriggerInteraction.Ignore))
            {
                if (nearbyCollider.GetComponentInParent<ITrap>() != null)
                {
                    return false;
                }
            }

            position = groundHit.point;
            normal = groundHit.normal;
            return true;
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
