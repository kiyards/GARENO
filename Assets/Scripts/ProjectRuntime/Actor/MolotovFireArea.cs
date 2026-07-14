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

        [SerializeField, Min(1)]
        private int fireAreaVfxCount = 5;

        [SerializeField, Range(0f, 1f)]
        private float fireAreaVfxScatterFraction = 0.8f;

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

            float lifetime = Mathf.Max(0.1f, fireTickCount * fireTickInterval);
            float scatterRadius = fireRadius * Mathf.Clamp01(fireAreaVfxScatterFraction);

            for (int i = 0; i < fireAreaVfxCount; i++)
            {
                Vector2 offset2D = Random.insideUnitCircle * scatterRadius;
                Vector3 spawnPosition = transform.position + new Vector3(offset2D.x, 0f, offset2D.y);
                GameObject vfx = Instantiate(fireAreaVfxPrefab, spawnPosition, Quaternion.identity);
                Object.Destroy(vfx, lifetime);
            }
        }
    }
}
