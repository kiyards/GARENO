using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectRuntime.Actor
{
    // Per-player controller for the Nemesis, living on GameplayPlayer alongside the turret/bear-trap
    // controllers. Spawns the Nemesis entity on activation and owns the reference to the active one.
    // Modelled on DungeonMasterTurretController, minus aiming/firing. Unlike the turret, the entity's
    // lifetime (not state exit) drives teardown: when the Nemesis is destroyed, DetachSpawnedNemesis
    // returns the Dungeon Master to top-down placement.
    public class DungeonMasterNemesisController : MonoBehaviour
    {
        private const float PlacementSampleRadius = 50f;

        private GameplayPlayer _player;
        private DungeonMasterNemesis _activeNemesis;

        public DungeonMasterNemesis ActiveNemesis => _activeNemesis;
        public bool HasActiveNemesis => _activeNemesis != null;
        public bool IsDisassembling => _activeNemesis != null && _activeNemesis.IsDisassembling;
        // Seconds left on the active Nemesis's lifetime (0 when none is active). Read by the HUD.
        public float ActiveLifetimeRemaining =>
            _activeNemesis != null ? _activeNemesis.LifetimeRemainingSeconds : 0f;
        // 1 at spawn → 0 at expiry; drives the HUD charge fill while controlling.
        public float ActiveLifetimeFraction =>
            _activeNemesis != null && _activeNemesis.LifetimeSeconds > 0f
                ? Mathf.Clamp01(_activeNemesis.LifetimeRemainingSeconds / _activeNemesis.LifetimeSeconds)
                : 0f;

        public void Initialize(GameplayPlayer owner)
        {
            _player = owner;
        }

        [Server]
        public bool ServerSpawnNemesis(Vector3 position)
        {
            var player = ResolvePlayer();
            if (player == null || !player.IsDungeonMaster || _activeNemesis != null)
            {
                return false;
            }

            GameObject prefab = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.DungeonMasterNemesisPrefab
                : null;

            if (prefab == null)
            {
                Debug.LogWarning(
                    "[DungeonMasterNemesisController] Nemesis prefab is null — assign it on GameNetworkManager."
                );
                return false;
            }

            if (!prefab.TryGetComponent(out DungeonMasterNemesis _))
            {
                Debug.LogWarning(
                    "[DungeonMasterNemesisController] Nemesis prefab must have a DungeonMasterNemesis component on its root."
                );
                return false;
            }

            if (!NavMesh.SamplePosition(
                    position,
                    out NavMeshHit navMeshHit,
                    PlacementSampleRadius,
                    NavMesh.AllAreas))
            {
                return false;
            }

            GameObject nemesisObject = Instantiate(
                prefab,
                navMeshHit.position,
                Quaternion.identity
            );
            var nemesis = nemesisObject.GetComponent<DungeonMasterNemesis>();
            nemesis.ServerInitialize(player);

            // Spawn with the Dungeon Master as owner so their client is authoritative over the Nemesis
            // NetworkTransform and drives its movement (matches the client-authoritative player model).
            NetworkServer.Spawn(nemesisObject, player.connectionToClient);
            AttachSpawnedNemesis(nemesis);
            return true;
        }

        [Server]
        public void ServerBeginDisassemble()
        {
            if (_activeNemesis == null)
            {
                return;
            }

            _activeNemesis.ServerBeginDisassemble();
        }

        // Re-validates the request server-side before delegating to the entity — mirrors
        // DungeonMasterTurretController.ServerFire's guard block.
        [Server]
        public void ServerExecuteAttack(NemesisAttackType type)
        {
            var player = ResolvePlayer();
            if (
                player == null
                || !player.IsDungeonMaster
                || !(player.currentState is DungeonMasterNemesisState)
                || _activeNemesis == null
            )
            {
                return;
            }

            _activeNemesis.ServerTryExecuteAttack(type);
        }

        public void AttachSpawnedNemesis(DungeonMasterNemesis nemesis)
        {
            var player = ResolvePlayer();
            if (player == null || nemesis == null || !nemesis.IsOwnedBy(player))
            {
                return;
            }

            _activeNemesis = nemesis;
        }

        public void DetachSpawnedNemesis(DungeonMasterNemesis nemesis)
        {
            if (_activeNemesis != nemesis)
            {
                return;
            }

            _activeNemesis = null;

            var player = ResolvePlayer();
            if (player == null)
            {
                return;
            }

            // Server restarts the Nemesis countdown so it can be used again — it is not one-time-use.
            if (player.isServer)
            {
                player.CardManager.ServerOnNemesisEnded();
            }

            if (player.isLocalPlayer
                && player.currentState is DungeonMasterNemesisState)
            {
                player.QueueState(new DungeonMasterMovementState(player));
            }
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
