using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ProjectRuntime.Actor
{
    public class DungeonMasterDoorInteractor : MonoBehaviour
    {
        private const string DoorLayerName = "Door";

        [SerializeField] private GameplayPlayer player;
        [SerializeField] private float maxInteractionDistance = 1000f;
        [SerializeField] private LayerMask interactionMask = Physics.DefaultRaycastLayers;

        private LockableDoor _hoveredDoor;
        private UILockableDoorHover _hoverUi;

        public void Initialize(GameplayPlayer owner)
        {
            player = owner;
            ConfigureInteractionMask();
        }

        private void Awake()
        {
            player ??= GetComponent<GameplayPlayer>();
            ConfigureInteractionMask();
        }

        private void Update()
        {
            if (!CanInteract())
            {
                SetHoveredDoor(null);
                return;
            }

            SetHoveredDoor(TryGetHoveredDoor());

            if (_hoveredDoor != null && player.input != null && player.input.ClickPress && !IsPointerOverUi())
            {
                player.CmdTryLockDoor(_hoveredDoor.netId);
            }
        }

        private bool CanInteract()
        {
            return player != null &&
                   player.isLocalPlayer &&
                   player.IsDungeonMaster &&
                   player.currentState is DungeonMasterMovementState;
        }

        private LockableDoor TryGetHoveredDoor()
        {
            if (IsPointerOverUi() ||
                !CursorPlacementUtility.TryGetCursorRay(out Ray ray) ||
                !Physics.Raycast(
                    ray,
                    out RaycastHit hit,
                    maxInteractionDistance,
                    interactionMask,
                    QueryTriggerInteraction.Collide))
            {
                return null;
            }

            return hit.collider.GetComponentInParent<LockableDoor>();
        }

        private void ConfigureInteractionMask()
        {
            int doorLayer = LayerMask.NameToLayer(DoorLayerName);
            if (doorLayer >= 0)
            {
                interactionMask = 1 << doorLayer;
            }
        }

        private void SetHoveredDoor(LockableDoor door)
        {
            _hoveredDoor = door;

            if (_hoveredDoor == null)
            {
                _hoverUi?.SetDoor(null);
                return;
            }

            if (_hoverUi == null)
            {
                _hoverUi = UILockableDoorHover.Ensure();
            }

            _hoverUi.SetDoor(_hoveredDoor);
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
