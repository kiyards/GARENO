using UnityEngine;
using UnityEngine.VFX;

namespace ProjectRuntime.Combat
{
    /// <summary>
    /// Non-networked hit VFX (e.g. blood spray) instantiated client-side at a world position.
    /// If the prefab exposes a "BloodVelocity" VFX Graph property, its horizontal direction is
    /// redirected to point back at the shooter while keeping the graph's authored horizontal
    /// speed and vertical (arc) velocity untouched.
    /// </summary>
    public static class HitVfx
    {
        private static readonly int BloodVelocityId = Shader.PropertyToID("BloodVelocity");

        public static void Play(GameObject prefab, Vector3 worldPos, Vector3 fireDirection, float lifetime)
        {
            if (prefab == null)
                return;

            var vfx = Object.Instantiate(prefab, worldPos, Quaternion.identity);

            var visualEffect = vfx.GetComponentInChildren<VisualEffect>();
            if (visualEffect != null && visualEffect.HasVector3(BloodVelocityId))
            {
                Vector3 authored = visualEffect.GetVector3(BloodVelocityId);
                float horizontalSpeed = new Vector2(authored.x, authored.z).magnitude;

                Vector3 back = new Vector3(-fireDirection.x, 0f, -fireDirection.z);
                back = back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.zero;

                visualEffect.SetVector3(
                    BloodVelocityId,
                    new Vector3(back.x * horizontalSpeed, authored.y, back.z * horizontalSpeed)
                );
            }

            Object.Destroy(vfx, lifetime);
        }

        /// <summary>Non-organic impact VFX (sparks, dust, etc.) oriented along the surface normal
        /// of whatever was hit — traps, turrets, the crystal.</summary>
        public static void PlayImpact(GameObject prefab, Vector3 worldPos, Vector3 surfaceNormal, float lifetime)
        {
            if (prefab == null)
                return;

            Quaternion rotation = surfaceNormal.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(surfaceNormal)
                : Quaternion.identity;

            var vfx = Object.Instantiate(prefab, worldPos, rotation);
            Object.Destroy(vfx, lifetime);
        }
    }
}
