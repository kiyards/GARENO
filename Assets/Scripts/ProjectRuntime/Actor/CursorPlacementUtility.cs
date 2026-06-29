using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRuntime.Actor
{
    public static class CursorPlacementUtility
    {
        public static bool TryGetCursorRay(out Ray ray, Camera camera = null)
        {
            ray = default;

            if (Mouse.current == null)
            {
                return false;
            }

            camera ??= Camera.main;
            if (camera == null)
            {
                return false;
            }

            ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            return true;
        }

        public static bool TryGetPlacementFromCursor(
            float maxDistance,
            LayerMask placementMask,
            out Vector3 position,
            out Vector3 normal,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore,
            bool fallbackToWorldGroundPlane = true)
        {
            position = Vector3.zero;
            normal = Vector3.up;

            if (!TryGetCursorRay(out Ray ray))
            {
                return false;
            }

            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, placementMask, triggerInteraction))
            {
                position = hit.point;
                normal = hit.normal;
                return true;
            }

            if (!fallbackToWorldGroundPlane)
            {
                return false;
            }

            var groundPlane = new Plane(Vector3.up, Vector3.zero);
            if (!groundPlane.Raycast(ray, out float enter) || enter > maxDistance)
            {
                return false;
            }

            position = ray.GetPoint(enter);
            normal = Vector3.up;
            return true;
        }
    }
}
