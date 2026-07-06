using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Rigidbody))]
    public class FlashbangTrap : NetworkBehaviour, ITrap
    {
        [Header("Flash")]
        [SerializeField] private float fuseDelay = 1.5f;
        [SerializeField] private float blindRadius = 15f;
        [SerializeField] private float maxBlindDuration = 3.5f;
        [SerializeField] private LayerMask obstacleMask;

        private bool _bounced;
        // SyncVar so OnStopClient can read it when the object is destroyed (same pattern as C4Trap._exploded).
        [SyncVar] private bool _flashed;

        public override void OnStartServer()
        {
            base.OnStartServer();
            _bounced = false;
            _flashed = false;
            // Prefab defaults to kinematic=true like other traps; server enables physics for flight.
            if (TryGetComponent(out Rigidbody rb))
            {
                rb.useGravity = true;
                rb.isKinematic = false;
            }
        }

        public override void OnStopClient()
        {
            base.OnStopClient();
            if (_flashed) SpawnFlashVfx();
        }

        [ServerCallback]
        private void OnCollisionEnter(Collision col)
        {
            if (_bounced) return;
            _bounced = true;
            StartCoroutine(FlashSequence());
        }

        [Server]
        private IEnumerator FlashSequence()
        {
            yield return new WaitForSeconds(fuseDelay);
            ServerFlash();
        }

        [Server]
        private void ServerFlash()
        {
            if (_flashed) return;
            _flashed = true;

            Vector3 center = transform.position;
            var hitPlayers = new HashSet<GameplayPlayer>();

            foreach (Collider col in Physics.OverlapSphere(
                center, blindRadius, Physics.AllLayers, QueryTriggerInteraction.Ignore))
            {
                var player = col.GetComponentInParent<GameplayPlayer>();
                if (player == null || hitPlayers.Contains(player) || !IsValidSurvivor(player))
                    continue;

                hitPlayers.Add(player);

                Vector3 eyePos = player.transform.position + Vector3.up * 1.6f;
                if (Physics.Linecast(center, eyePos, obstacleMask))
                    continue;

                float distance = Vector3.Distance(center, eyePos);
                float distanceFalloff = 1f - Mathf.Clamp01(distance / blindRadius);
                // Angle falloff is calculated client-side in TargetApplyFlash from the real camera direction.
                player.ServerApplyFlash(center, maxBlindDuration * distanceFalloff);
            }

            NetworkServer.Destroy(gameObject);
        }

        private void SpawnFlashVfx()
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "FlashbangVfx";
            sphere.transform.position = transform.position;
            sphere.transform.localScale = Vector3.one * 1.5f;
            if (sphere.TryGetComponent(out Collider c)) Destroy(c);
            if (sphere.TryGetComponent(out Renderer r))
                r.material.color = new Color(1f, 1f, 0.9f, 1f);
            Destroy(sphere, 0.15f);
        }

        private static bool IsValidSurvivor(GameplayPlayer player)
        {
            if (player == null || player.IsDungeonMaster || player.IsInactive) return false;
            if (player.localManager.playerRole != PlayerRole.Survivor) return false;
            return player.health.IsAlive;
        }
    }
}
