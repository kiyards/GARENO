using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Combat;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public class MolotovFireArea : NetworkBehaviour
    {
        [Header("Gameplay")]
        [SerializeField]
        private float fireRadius = 4.25f;

        [SerializeField]
        private float fireDamagePerTick = 18f;

        [SerializeField]
        private float fireTickInterval = 1f;

        [SerializeField]
        private int fireTickCount = 5;

        [SerializeField]
        private LayerMask damageLayers = Physics.AllLayers;

        [Header("Visuals")]
        [SerializeField]
        private GameObject fireAreaVfxPrefab;

        private GameplayPlayer _owner;

        [Server]
        public void ServerInitialize(GameplayPlayer owner)
        {
            _owner = owner;
            StartCoroutine(ServerBurnRoutine());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SpawnFireAreaVfx();
        }

        [Server]
        private IEnumerator ServerBurnRoutine()
        {
            for (int tickIndex = 0; tickIndex < fireTickCount; tickIndex++)
            {
                yield return new WaitForSeconds(fireTickInterval);
                ApplyFireTick(transform.position);
            }

            NetworkServer.Destroy(gameObject);
        }

        [Server]
        private void ApplyFireTick(Vector3 center)
        {
            var damagedTargets = new HashSet<IDamageable>();
            foreach (
                Collider hit in Physics.OverlapSphere(
                    center,
                    fireRadius,
                    damageLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                IDamageable damageable =
                    hit.GetComponentInParent<IDamageable>()
                    ?? hit.GetComponentInChildren<IDamageable>();
                if (damageable == null || !damageable.IsAlive || !damagedTargets.Add(damageable))
                {
                    continue;
                }

                damageable.ServerTakeDamage(
                    fireDamagePerTick,
                    _owner != null ? _owner.netId : 0,
                    center
                );
            }
        }

        private void SpawnFireAreaVfx()
        {
            if (fireAreaVfxPrefab == null)
            {
                return;
            }

            GameObject vfx = Instantiate(fireAreaVfxPrefab, transform.position, Quaternion.identity);
            float lifetime = Mathf.Max(0.1f, fireTickCount * fireTickInterval);
            Object.Destroy(vfx, lifetime);
        }
    }
}
