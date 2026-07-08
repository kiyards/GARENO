using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Rigidbody))]
    public class C4Trap : NetworkBehaviour, ITrap
    {
        [Header("Health")]
        [SerializeField]
        private float maxHealth = 500f;

        [Header("Explosion")]
        [SerializeField]
        private float explosionDamage = 50f;

        [SerializeField]
        private float explosionRadius = 6f;

        [SerializeField]
        private float knockbackForce = 10f;

        [SerializeField]
        private float immobilizeDuration = 1.5f;

        [Header("Flash Sequence")]
        [SerializeField]
        private float flashInterval = 1.5f;

        [SerializeField]
        private float flashDuration = 0.25f;

        [Header("VFX")]
        [SerializeField]
        private GameObject explosionVfxPrefab;

        [SerializeField]
        private float explosionVfxLifetime = 3f;

        [SyncVar(hook = nameof(OnArmedSynced))]
        private bool _isArmed;

        private Health _health;
        private Renderer[] _renderers;

        [SyncVar]
        private bool _exploded;

        public bool IsArmed => _isArmed;

        private void Awake()
        {
            CacheComponents();
            ConfigureComponents();
            ApplyArmedVisual(_isArmed);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            CacheComponents();
            ConfigureComponents();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            CacheComponents();
            ConfigureComponents();
            _isArmed = false;
            _exploded = false;

            _health.OnDeathEvent += OnHealthDepleted;
        }

        public override void OnStopServer()
        {
            _health.OnDeathEvent -= OnHealthDepleted;
            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            CacheComponents();
            ApplyArmedVisual(_isArmed);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || _isArmed || _exploded)
                return;

            var player = other.GetComponentInParent<GameplayPlayer>();
            if (!IsValidSurvivor(player))
                return;

            ServerArm();
        }

        [Server]
        private void ServerArm()
        {
            _isArmed = true;
            StartCoroutine(ServerFlashSequence());
        }

        [Server]
        private IEnumerator ServerFlashSequence()
        {
            RpcFlashRed(flashDuration);
            yield return new WaitForSeconds(flashInterval);

            RpcFlashRed(flashDuration);
            yield return new WaitForSeconds(flashInterval);

            ServerExplode();
        }

        [ClientRpc]
        private void RpcFlashRed(float duration)
        {
            StartCoroutine(FlashRedCoroutine(duration));
        }

        private IEnumerator FlashRedCoroutine(float duration)
        {
            SetAllRenderersColor(new Color(1f, 0.1f, 0.1f, 1f));
            yield return new WaitForSeconds(duration);
            ApplyArmedVisual(_isArmed);
        }

        [Server]
        private void ServerExplode()
        {
            if (_exploded)
                return;
            _exploded = true;

            var hitPlayers = new HashSet<GameplayPlayer>();
            Vector3 center = transform.position;

            foreach (
                Collider col in Physics.OverlapSphere(
                    center,
                    explosionRadius,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                var player = col.GetComponentInParent<GameplayPlayer>();
                if (player == null || hitPlayers.Contains(player) || !IsValidSurvivor(player))
                    continue;

                hitPlayers.Add(player);

                player.health.ServerTakeDamage(explosionDamage, netId, center);

                Vector3 toPlayer = player.transform.position - center;
                float distance = toPlayer.magnitude;
                float falloff = 1f - Mathf.Clamp01(distance / explosionRadius);
                Vector3 impulse =
                    (distance > 0.01f ? toPlayer.normalized : Vector3.up)
                    * knockbackForce
                    * falloff;
                player.ServerApplyKnockback(impulse);
                player.ServerImmobilize(immobilizeDuration);
            }

            if (isClient)
            {
                // Host is its own client too — play now rather than via the queued Rpc below,
                // which would otherwise race with NetworkServer.Destroy() unspawning this object.
                HitVfx.PlayAt(explosionVfxPrefab, transform.position, explosionVfxLifetime);
            }

            RpcPlayExplosionVfx(transform.position);
            NetworkServer.Destroy(gameObject);
        }

        [ClientRpc]
        private void RpcPlayExplosionVfx(Vector3 worldPos)
        {
            if (isServer)
                return; // Host already played this above.

            HitVfx.PlayAt(explosionVfxPrefab, worldPos, explosionVfxLifetime);
        }

        [Server]
        private void OnHealthDepleted(uint killerNetId)
        {
            ServerExplode();
        }

        private bool IsValidSurvivor(GameplayPlayer player)
        {
            if (player == null || player.IsDungeonMaster || player.IsInactive)
                return false;

            if (player.localManager.playerRole != PlayerRole.Survivor)
                return false;

            return player.health.IsAlive;
        }

        private void OnArmedSynced(bool oldValue, bool newValue)
        {
            ApplyArmedVisual(newValue);
        }

        private void CacheComponents()
        {
            _health ??= GetComponent<Health>();
            if (_renderers == null || _renderers.Length == 0)
                _renderers = GetComponentsInChildren<Renderer>(true);
        }

        private void ConfigureComponents()
        {
            if (_health != null)
                _health.ConfigureMaxHealth(maxHealth);

            if (TryGetComponent(out Rigidbody rb))
            {
                rb.useGravity = false;
                rb.isKinematic = true;
            }
        }

        private void ApplyArmedVisual(bool armed)
        {
            Color targetColor = armed
                ? new Color(0.55f, 0.45f, 0.1f, 1f) // dim amber: counting down between flashes
                : new Color(0.15f, 0.55f, 0.15f, 1f); // green: unarmed
            SetAllRenderersColor(targetColor);
        }

        private void SetAllRenderersColor(Color color)
        {
            CacheComponents();
            foreach (Renderer r in _renderers)
                if (r != null)
                    r.material.color = color;
        }
    }
}
