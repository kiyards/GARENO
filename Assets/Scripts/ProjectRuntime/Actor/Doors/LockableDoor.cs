using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectRuntime.Actor
{
    public enum LockableDoorState
    {
        Unlocked,
        Locking,
        Locked,
        Unlocking,
        Cooldown,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public class LockableDoor : NetworkBehaviour
    {
        private const string DoorLayerName = "Door";

        [Header("Scene References")]
        [SerializeField] private Transform doorRoot;
        [SerializeField] private Transform unlockedPose;
        [SerializeField] private Transform lockedPose;
        [SerializeField] private Collider interactionCollider;
        [SerializeField] private Collider pushVolume;

        [Header("Timing")]
        [SerializeField] private float lockDuration = 10f;
        [SerializeField] private float cooldownDuration = 10f;
        [SerializeField] private float moveDuration = 1f;

        [Header("Push")]
        [SerializeField] private LayerMask pushMask = Physics.DefaultRaycastLayers;
        [SerializeField] private float pushSpeed = 6f;

        [SyncVar(hook = nameof(OnDoorStateSynced))]
        private LockableDoorState state = LockableDoorState.Unlocked;

        [SyncVar(hook = nameof(OnTimingSynced))]
        private double phaseStartTime;

        [SyncVar(hook = nameof(OnTimingSynced))]
        private double phaseEndTime;

        [SyncVar(hook = nameof(OnTimingSynced))]
        private double cooldownEndTime;

        public LockableDoorState State => state;
        public bool CanBeLocked => state == LockableDoorState.Unlocked &&
                                   NetworkTime.time >= cooldownEndTime;

        private void Awake()
        {
            ApplyDoorLayer();
            ConfigureNavMeshObstacle();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            state = LockableDoorState.Unlocked;
            phaseStartTime = 0d;
            phaseEndTime = 0d;
            cooldownEndTime = 0d;
            ApplyDoorPose();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyDoorPose();
        }

        private void Update()
        {
            if (isServer)
            {
                ServerTickState();
                if (state == LockableDoorState.Locking || state == LockableDoorState.Unlocking)
                {
                    ServerPushSurvivors();
                }
            }

            ApplyDoorPose();
        }

        [Server]
        public bool ServerTryLock(PlayerManager requester)
        {
            if (requester == null ||
                requester.playerRole != PlayerRole.DungeonMaster ||
                !CanBeLocked ||
                BattleManager.Instance != null &&
                BattleManager.Instance.CurrentRoundPhase == RoundPhase.RoundComplete)
            {
                return false;
            }

            BeginTimedState(LockableDoorState.Locking, moveDuration);
            return true;
        }

        public string GetHoverText()
        {
            return state switch
            {
                LockableDoorState.Locked => $"Locked {GetRemainingPhaseSeconds():0.0}s",
                LockableDoorState.Cooldown => $"Cooldown {GetRemainingCooldownSeconds():0.0}s",
                LockableDoorState.Locking => "Moving",
                LockableDoorState.Unlocking => "Moving",
                _ => "Unlocked",
            };
        }

        [Server]
        private void ServerTickState()
        {
            double now = NetworkTime.time;

            switch (state)
            {
                case LockableDoorState.Locking:
                    if (now >= phaseEndTime)
                    {
                        BeginTimedState(LockableDoorState.Locked, lockDuration);
                    }
                    break;

                case LockableDoorState.Locked:
                    if (now >= phaseEndTime)
                    {
                        BeginTimedState(LockableDoorState.Unlocking, moveDuration);
                    }
                    break;

                case LockableDoorState.Unlocking:
                    if (now >= phaseEndTime)
                    {
                        BeginCooldown();
                    }
                    break;

                case LockableDoorState.Cooldown:
                    if (now >= cooldownEndTime)
                    {
                        state = LockableDoorState.Unlocked;
                        phaseStartTime = 0d;
                        phaseEndTime = 0d;
                    }
                    break;
            }
        }

        [Server]
        private void BeginTimedState(LockableDoorState nextState, float duration)
        {
            double now = NetworkTime.time;
            state = nextState;
            phaseStartTime = now;
            phaseEndTime = now + Mathf.Max(0.01f, duration);
        }

        [Server]
        private void BeginCooldown()
        {
            double now = NetworkTime.time;
            state = LockableDoorState.Cooldown;
            phaseStartTime = now;
            phaseEndTime = now + Mathf.Max(0f, cooldownDuration);
            cooldownEndTime = phaseEndTime;
        }

        private void ApplyDoorPose()
        {
            float lockedAmount = GetLockedAmount();
            doorRoot.SetPositionAndRotation(
                Vector3.Lerp(unlockedPose.position, lockedPose.position, lockedAmount),
                Quaternion.Slerp(unlockedPose.rotation, lockedPose.rotation, lockedAmount));
        }

        private void ApplyDoorLayer()
        {
            int doorLayer = LayerMask.NameToLayer(DoorLayerName);
            if (doorLayer < 0)
            {
                return;
            }

            SetLayerRecursive(transform, doorLayer);
        }

        private static void SetLayerRecursive(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            for (int i = 0; i < target.childCount; i++)
            {
                SetLayerRecursive(target.GetChild(i), layer);
            }
        }

        private void ConfigureNavMeshObstacle()
        {
            var obstacle = doorRoot.GetComponent<NavMeshObstacle>();
            if (obstacle == null)
            {
                obstacle = doorRoot.gameObject.AddComponent<NavMeshObstacle>();
            }

            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = Vector3.zero;
            obstacle.size = Vector3.one;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = false;
        }

        private float GetLockedAmount()
        {
            return state switch
            {
                LockableDoorState.Locking => GetPhaseProgress(),
                LockableDoorState.Locked => 1f,
                LockableDoorState.Unlocking => 1f - GetPhaseProgress(),
                _ => 0f,
            };
        }

        private float GetPhaseProgress()
        {
            double duration = phaseEndTime - phaseStartTime;
            if (duration <= 0d)
            {
                return 1f;
            }

            return Mathf.Clamp01((float)((NetworkTime.time - phaseStartTime) / duration));
        }

        private float GetRemainingPhaseSeconds()
        {
            return Mathf.Max(0f, (float)(phaseEndTime - NetworkTime.time));
        }

        private float GetRemainingCooldownSeconds()
        {
            return Mathf.Max(0f, (float)(cooldownEndTime - NetworkTime.time));
        }

        [Server]
        private void ServerPushSurvivors()
        {
            Bounds bounds = pushVolume.bounds;
            Collider[] hits = Physics.OverlapBox(
                bounds.center,
                bounds.extents,
                pushVolume.transform.rotation,
                pushMask,
                QueryTriggerInteraction.Ignore);

            Vector3 pushDirection = GetDoorTravelDirection();
            if (state == LockableDoorState.Unlocking)
            {
                pushDirection = -pushDirection;
            }

            foreach (Collider hit in hits)
            {
                var player = hit.GetComponentInParent<GameplayPlayer>();
                if (!IsPushTarget(player))
                {
                    continue;
                }

                Vector3 direction = pushDirection;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    direction = player.transform.position - pushVolume.bounds.center;
                    direction.y = 0f;
                }

                if (direction.sqrMagnitude < 0.0001f)
                {
                    direction = transform.forward;
                }

                Vector3 delta = direction.normalized * pushSpeed * Time.deltaTime;
                if (player.rb != null)
                {
                    player.rb.MovePosition(player.rb.position + delta);
                }
                else
                {
                    player.transform.position += delta;
                }
            }
        }

        private Vector3 GetDoorTravelDirection()
        {
            Vector3 direction = lockedPose.position - unlockedPose.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
        }

        private static bool IsPushTarget(GameplayPlayer player)
        {
            return player != null &&
                   player.localManager != null &&
                   player.localManager.playerRole == PlayerRole.Survivor &&
                   !player.IsInactive &&
                   player.health != null &&
                   player.health.IsAlive;
        }

        private void OnDoorStateSynced(LockableDoorState oldValue, LockableDoorState newValue)
        {
            ApplyDoorPose();
        }

        private void OnTimingSynced(double oldValue, double newValue)
        {
            ApplyDoorPose();
        }

        private void OnDrawGizmosSelected()
        {
            if (unlockedPose != null && lockedPose != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(unlockedPose.position, lockedPose.position);
                Gizmos.DrawWireSphere(unlockedPose.position, 0.15f);
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(lockedPose.position, 0.15f);
            }

            if (pushVolume != null)
            {
                Gizmos.color = new Color(1f, 0.8f, 0.1f, 0.25f);
                Gizmos.matrix = pushVolume.transform.localToWorldMatrix;
                if (pushVolume is BoxCollider box)
                {
                    Gizmos.DrawWireCube(box.center, box.size);
                }
            }
        }
    }
}
