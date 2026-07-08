using System.Collections;
using UnityEngine;

namespace ProjectRuntime.Combat
{
    public static class BulletTracer
    {
        private static Material _sharedTracerMaterial;

        // Spawns a short bright segment at `start` that flies toward `end` at `speed` and is
        // destroyed on arrival — a visualized projectile rather than a beam glued to the muzzle,
        // so it never freezes as a stale line if the shooter moves after firing.
        public static void Spawn(MonoBehaviour host, Vector3 start, Vector3 end, float speed, float segmentLength,
            float width, Color color, Material material = null, string objectName = "BulletTracer")
        {
            if (host == null)
            {
                return;
            }

            host.StartCoroutine(TracerRoutine(start, end, Mathf.Max(0.01f, speed),
                Mathf.Max(0.01f, segmentLength), Mathf.Max(0.001f, width), color, material, objectName));
        }

        private static IEnumerator TracerRoutine(Vector3 start, Vector3 end, float speed, float segmentLength,
            float width, Color color, Material material, string objectName)
        {
            Vector3 travel = end - start;
            float totalDistance = travel.magnitude;
            if (totalDistance < 0.001f)
            {
                yield break;
            }

            Vector3 direction = travel / totalDistance;

            var tracerObject = new GameObject(objectName);
            var line = tracerObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;

            Material resolvedMaterial = ResolveTracerMaterial(material);
            if (resolvedMaterial != null)
            {
                line.material = resolvedMaterial;
            }

            float traveled = 0f;
            while (traveled < totalDistance)
            {
                traveled += speed * Time.deltaTime;
                float headDistance = Mathf.Min(traveled, totalDistance);
                Vector3 head = start + direction * headDistance;
                Vector3 tail = start + direction * Mathf.Max(0f, headDistance - segmentLength);
                line.SetPosition(0, tail);
                line.SetPosition(1, head);
                yield return null;
            }

            Object.Destroy(tracerObject);
        }

        private static Material ResolveTracerMaterial(Material overrideMaterial)
        {
            if (overrideMaterial != null)
            {
                return overrideMaterial;
            }

            if (_sharedTracerMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Unlit/Color");
                }

                if (shader == null)
                {
                    shader = Shader.Find("Hidden/Internal-Colored");
                }

                if (shader != null)
                {
                    _sharedTracerMaterial = new Material(shader);
                }
            }

            return _sharedTracerMaterial;
        }
    }
}
