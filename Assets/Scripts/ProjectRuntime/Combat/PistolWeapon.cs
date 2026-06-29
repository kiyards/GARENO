using System;
using System.Collections;
using Mirror;
using ProjectRuntime.Actor;
using ProjectRuntime.Network;
using ProjectRuntime.UI;
using UnityEngine;

namespace ProjectRuntime.Combat
{
    /// <summary>
    /// Survivor pistol. Server-authoritative: the local client raycasts (it owns the camera/aim)
    /// and proposes a hit via <see cref="CmdFire"/>; the server validates, decrements ammo and
    /// applies damage. Infinite reserve ammo (reload always refills the magazine).
    /// </summary>
    public class PistolWeapon : NetworkBehaviour
    {
        [Header("Components")]
        [SerializeField]
        private GameplayPlayer player;

        [SerializeField]
        private CameraController cam;

        [SerializeField]
        private PlayerInput input;

        [Header("Config")]
        [SerializeField]
        private float damage = 100f;

        [SerializeField]
        private int magazineSize = 6;

        [SerializeField]
        private float maxRange = 100f;

        [SerializeField]
        private float reloadDuration = 1.5f;

        [SerializeField]
        private float fireCooldown = 0.25f;

        [Header("FX")]
        [SerializeField]
        private DamagePopup damagePopupPrefab;

        [SerializeField]
        private float tracerDuration = 0.08f;

        [SerializeField]
        private float tracerWidth = 0.025f;

        [SerializeField]
        private Color tracerColor = new(1f, 0.76f, 0.22f, 1f);

        [SerializeField]
        private Material tracerMaterial;

        [SyncVar(hook = nameof(OnAmmoSynced))]
        private int currentAmmo;

        [SyncVar]
        private bool isReloading;

        // Separate cooldown clocks: on the host the same instance is both client and server,
        // so a shared field would let the client's TryFire stamp block the server's CmdFire.
        private double _clientLastFireTime;
        private double _serverLastFireTime;
        private Coroutine _reloadRoutine;

        /// <summary>(current, magazineSize) — UI subscribes for the ammo readout.</summary>
        public event Action<int, int> OnAmmoChangedEvent;

        public int CurrentAmmo => currentAmmo;
        public int MagazineSize => magazineSize;
        public bool IsReloading => isReloading;

        public override void OnStartServer()
        {
            base.OnStartServer();
            currentAmmo = magazineSize;
        }

        private void Update()
        {
            if (!isLocalPlayer)
                return;
            if (player == null || player.IsInactive || input == null)
                return;
            if (player.IsDungeonMaster)
                return;

            if (input.ClickHold)
                TryFire();
            else if (input.ReloadPress)
                TryReload();
        }

        private void TryFire()
        {
            if (isReloading)
                return;
            if (currentAmmo <= 0)
            {
                TryReload();
                return;
            }
            if (NetworkTime.time - _clientLastFireTime < fireCooldown)
                return;
            _clientLastFireTime = NetworkTime.time;

            // Fallback hit point along the aim ray when nothing is hit.
            cam.GetAimData(maxRange, out Vector3 origin, out Vector3 dir, out _);
            uint targetNetId = 0;
            Vector3 hitPoint = origin + dir * maxRange;

            var hits = cam.GetRaycastData(maxRange);
            if (hits.Count > 0)
            {
                var first = hits[0];
                hitPoint = first.hitPoint;
                var identity = first.hit.collider.GetComponentInParent<NetworkIdentity>();
                if (identity != null)
                    targetNetId = identity.netId;
            }

            CmdFire(targetNetId, hitPoint, origin);
        }

        private void TryReload()
        {
            if (isReloading || currentAmmo >= magazineSize)
                return;
            CmdReload();
        }

        [Command]
        private void CmdFire(uint targetNetId, Vector3 hitPoint, Vector3 tracerStart)
        {
            if (player != null && player.IsDungeonMaster)
                return;
            if (isReloading || currentAmmo <= 0)
                return;
            if (NetworkTime.time - _serverLastFireTime < fireCooldown - 0.05f)
                return; // loose server gate
            _serverLastFireTime = NetworkTime.time;

            // Loose range sanity check against the networked player position
            // (the server's local camera transform is not driven for remote players).
            if (Vector3.Distance(transform.position, hitPoint) > maxRange * 1.1f)
                return;

            currentAmmo--;
            RpcShowBulletTracer(tracerStart, hitPoint);

            if (
                targetNetId != 0
                && NetworkServer.spawned.TryGetValue(targetNetId, out var targetIdentity)
            )
            {
                var damageable =
                    targetIdentity.GetComponentInParent<IDamageable>()
                    ?? targetIdentity.GetComponentInChildren<IDamageable>();
                if (
                    damageable != null
                    && damageable.IsAlive
                    && !IsBlockedByFriendlyFire(targetIdentity)
                )
                {
                    damageable.ServerTakeDamage(damage, netId, hitPoint);
                    RpcShowDamageNumber(hitPoint, damage);
                }
            }
        }

        /// <summary>GDD: a survivor shooting another survivor is blocked (no damage).</summary>
        [Server]
        private bool IsBlockedByFriendlyFire(NetworkIdentity target)
        {
            var shooterManager = player != null ? player.localManager : null;
            var targetManager = target.GetComponentInParent<PlayerManager>();
            if (shooterManager == null || targetManager == null)
                return false;

            return shooterManager.playerRole == PlayerRole.Survivor
                && targetManager.playerRole == PlayerRole.Survivor;
        }

        [Command]
        private void CmdReload()
        {
            if (player != null && player.IsDungeonMaster)
                return;
            if (isReloading || currentAmmo >= magazineSize)
                return;
            if (_reloadRoutine != null)
                StopCoroutine(_reloadRoutine);
            _reloadRoutine = StartCoroutine(ServerReloadRoutine());
        }

        [Server]
        private IEnumerator ServerReloadRoutine()
        {
            isReloading = true;
            yield return new WaitForSeconds(reloadDuration);
            currentAmmo = magazineSize;
            isReloading = false;
            _reloadRoutine = null;
        }

        [ClientRpc]
        private void RpcShowDamageNumber(Vector3 worldPos, float amount)
        {
            DamagePopup.Spawn(damagePopupPrefab, worldPos, amount);
        }

        [ClientRpc]
        private void RpcShowBulletTracer(Vector3 start, Vector3 end)
        {
            BulletTracer.Spawn(
                this,
                start,
                end,
                tracerDuration,
                tracerWidth,
                tracerColor,
                tracerMaterial,
                "PistolBulletTracer"
            );
        }

        private void OnAmmoSynced(int oldValue, int newValue)
        {
            OnAmmoChangedEvent?.Invoke(newValue, magazineSize);
        }
    }
}
