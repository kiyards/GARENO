using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Combat;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Rigidbody))]
    public class MolotovProjectile : NetworkBehaviour
    {
        [Header("Gameplay")]
        [SerializeField]
        private LayerMask explosionContactLayers = Physics.DefaultRaycastLayers;

        [SerializeField]
        private float projectileLifetime = 8f;

        [Header("Visuals")]
        [SerializeField]
        private MolotovFireArea fireAreaPrefab;

        private GameplayPlayer _owner;
        private Rigidbody _body;
        private Collider _projectileCollider;
        private bool _hasActivated;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnValidate()
        {
            CacheReferences();
        }

        [Server]
        public void ServerInitialize(GameplayPlayer owner, Vector3 initialVelocity)
        {
            CacheReferences();
            _owner = owner;

            if (_owner != null && _projectileCollider != null)
            {
                foreach (Collider ownerCollider in _owner.GetComponentsInChildren<Collider>(true))
                {
                    if (ownerCollider != null)
                    {
                        Physics.IgnoreCollision(_projectileCollider, ownerCollider, true);
                    }
                }
            }

            if (_body != null)
            {
                _body.linearVelocity = initialVelocity;
            }

            StartCoroutine(ServerLifetimeRoutine());
        }

        [ServerCallback]
        private void OnCollisionEnter(Collision collision)
        {
            if (_hasActivated)
            {
                return;
            }

            GameObject collidedObject = collision.gameObject;
            if (collidedObject == null)
            {
                return;
            }

            int collidedLayer = collidedObject.layer;
            if ((explosionContactLayers.value & (1 << collidedLayer)) == 0)
            {
                return;
            }

            _hasActivated = true;
            Vector3 impactPoint =
                collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;

            ServerSpawnFireArea(impactPoint);
            NetworkServer.Destroy(gameObject);
        }

        [Server]
        private void ServerSpawnFireArea(Vector3 impactPoint)
        {
            if (fireAreaPrefab == null)
            {
                Debug.LogWarning("[MolotovProjectile] Fire area prefab is not assigned.");
                return;
            }

            GameObject fireAreaObject = Instantiate(
                fireAreaPrefab.gameObject,
                impactPoint,
                Quaternion.identity
            );
            MolotovFireArea fireArea = fireAreaObject.GetComponent<MolotovFireArea>();
            if (fireArea == null)
            {
                Debug.LogWarning("[MolotovProjectile] Fire area prefab is missing MolotovFireArea.");
                Object.Destroy(fireAreaObject);
                return;
            }

            fireArea.ServerInitialize(_owner);
            NetworkServer.Spawn(fireAreaObject);
        }

        [Server]
        private IEnumerator ServerLifetimeRoutine()
        {
            yield return new WaitForSeconds(projectileLifetime);
            if (this != null && gameObject != null)
            {
                NetworkServer.Destroy(gameObject);
            }
        }

        private void CacheReferences()
        {
            _body ??= GetComponent<Rigidbody>();
            _projectileCollider ??= GetComponent<Collider>();
        }
    }
}
