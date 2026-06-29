using Mirror;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class DungeonMasterBearTrapController : MonoBehaviour
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

        public void TryPlace()
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
            player.CmdPlaceBearTrap(position, normal);
        }

        [Server]
        public void ServerPlace(Vector3 requestedPosition, Vector3 requestedNormal)
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

            GameObject trapPrefab = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.BearTrapPrefab
                : null;
            if (trapPrefab == null)
            {
                Debug.LogWarning("[DungeonMasterBearTrapController] Assign the bear trap prefab on GameNetworkManager.");
                return;
            }

            if (!trapPrefab.TryGetComponent(out BearTrap _))
            {
                Debug.LogWarning("[DungeonMasterBearTrapController] Bear trap prefab must have a BearTrap component on its root.");
                return;
            }

            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
            GameObject trapObject = Instantiate(trapPrefab, position, rotation);
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
                if (nearbyCollider.GetComponentInParent<BearTrap>() != null)
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
