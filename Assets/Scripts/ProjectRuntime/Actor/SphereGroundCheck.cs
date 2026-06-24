using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class SphereGroundCheck : MonoBehaviour
    {
        [SerializeField] LayerMask groundMask;
        [SerializeField] float radiusCheck;
        [SerializeField] float castDist;
        [SerializeField] float maxSlopeAngle = 45f;
        public bool IsGrounded;
        public Vector3 GroundedNormal;
        void FixedUpdate()
        {
            var groundHit = Physics.SphereCast(transform.position, radiusCheck, Vector3.down, out RaycastHit hit, castDist, groundMask);
            if (groundHit)
            {
                float angle = Vector3.Angle(hit.normal, Vector3.up);
                IsGrounded = angle <= maxSlopeAngle;
                GroundedNormal = hit.normal;
            }
            else
            {
                IsGrounded = false;
                GroundedNormal = Vector3.up;
            }
        }
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Vector3 center = transform.position + Vector3.down * castDist;
            Gizmos.DrawWireSphere(center, radiusCheck);
            Gizmos.DrawLine(center, center + GroundedNormal.normalized);
        }
    }
}
