using System.Collections;
using UnityEngine;

namespace ProjectRuntime.Combat
{
    public static class BulletTracer
    {
        private static Material _sharedTracerMaterial;

        public static void Spawn(MonoBehaviour host, Vector3 start, Vector3 end, float duration, float width,
            Color color, Material material = null, string objectName = "BulletTracer")
        {
            if (host == null)
            {
                return;
            }

            host.StartCoroutine(TracerRoutine(start, end, Mathf.Max(0.01f, duration),
                Mathf.Max(0.001f, width), color, material, objectName));
        }

        private static IEnumerator TracerRoutine(Vector3 start, Vector3 end, float duration, float width,
            Color color, Material material, string objectName)
        {
            var tracerObject = new GameObject(objectName);
            var line = tracerObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.startWidth = width;
            line.endWidth = width;

            Material resolvedMaterial = ResolveTracerMaterial(material);
            if (resolvedMaterial != null)
            {
                line.material = resolvedMaterial;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float alpha = Mathf.Lerp(1f, 0f, elapsed / duration);
                Color frameColor = color;
                frameColor.a *= alpha;
                line.startColor = frameColor;
                line.endColor = frameColor;
                elapsed += Time.deltaTime;
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
