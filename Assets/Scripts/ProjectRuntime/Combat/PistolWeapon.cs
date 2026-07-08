using System;
using System.Collections;
using Mirror;
using ProjectRuntime.Actor;
using ProjectRuntime.Managers;
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

        [SerializeField]
        private float shakeAmplitude = 0.8f;

        [SerializeField]
        private float shakeDuration = 0.3f;

        [Header("FX")]
        [SerializeField]
        private DamagePopup damagePopupPrefab;

        [SerializeField]
        private GameObject hitVfxPrefab;

        [SerializeField]
        private float hitVfxLifetime = 2f;

        [SerializeField]
        private GameObject impactVfxPrefab;

        [SerializeField]
        private float impactVfxLifetime = 2f;

        [SerializeField]
        private float tracerSpeed = 250f;

        [SerializeField]
        private float tracerSegmentLength = 3f;

        [SerializeField]
        private float tracerWidth = 0.02f;

        [SerializeField]
        private Color tracerColor = new Color(1f, 0.9f, 0.4f, 1f);

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
            if (player.IsBearTrapped)
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
            bool occluded = cam.GetAimData(
                maxRange,
                out Vector3 origin,
                out Vector3 dir,
                out RaycastHit occlusionHit
            );
            uint targetNetId = 0;
            Vector3 hitPoint = origin + dir * maxRange;
            Vector3 hitNormal = -dir;
            int hitLayer = -1;

            var hits = cam.GetRaycastData(maxRange);
            if (hits.Count > 0)
            {
                var first = hits[0];
                hitPoint = first.hitPoint;
                hitNormal = first.hit.normal;
                hitLayer = first.hit.collider.gameObject.layer;
                var identity = first.hit.collider.GetComponentInParent<NetworkIdentity>();
                if (identity != null)
                    targetNetId = identity.netId;
            }
            else if (occluded)
            {
                // Nothing shootable in range, but the aim ray is blocked (e.g. a wall or the
                // ground) — use that occluder so environment impact VFX still lands correctly.
                hitPoint = occlusionHit.point;
                hitNormal = occlusionHit.normal;
                hitLayer = occlusionHit.collider.gameObject.layer;
            }

            Vector3 muzzlePosition = cam.GetWeaponMuzzlePosition();

            // Play the shooter's own tracer immediately instead of waiting on the Cmd/Rpc round
            // trip — RpcPlayTracer (includeOwner = false) still covers everyone else.
            PlayTracer(muzzlePosition, hitPoint);

            CmdFire(targetNetId, muzzlePosition, hitPoint, dir, hitNormal, hitLayer);
            cam.AddShake(shakeAmplitude, shakeDuration);
        }

        private void TryReload()
        {
            if (isReloading || currentAmmo >= magazineSize)
                return;
            CmdReload();
        }

        [Command]
        private void CmdFire(
            uint targetNetId,
            Vector3 muzzlePosition,
            Vector3 hitPoint,
            Vector3 fireDirection,
            Vector3 hitNormal,
            int hitLayer
        )
        {
            if (player != null && (player.IsDungeonMaster || player.IsBearTrapped))
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
            RpcPlayShootAudio(transform.position);
            RpcPlayTracer(muzzlePosition, hitPoint);

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
                    if (IsOrganicTarget(targetIdentity))
                        RpcPlayHitVfx(hitPoint, fireDirection);
                    else
                        RpcPlayImpactVfx(hitPoint, hitNormal);
                    return;
                }
            }

            if (hitLayer >= 0)
            {
                RpcPlayImpactVfx(hitPoint, hitNormal);
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

        /// <summary>Blood VFX only makes sense on zombies — not traps, turrets, or the crystal.
        /// The pistol can't hit other survivors (blocked by friendly fire).</summary>
        private static bool IsOrganicTarget(NetworkIdentity target)
        {
            return target.GetComponentInParent<ZombieEnemy>() != null;
        }

        [Command]
        private void CmdReload()
        {
            if (player != null && (player.IsDungeonMaster || player.IsBearTrapped))
                return;
            if (isReloading || currentAmmo >= magazineSize)
                return;
            if (_reloadRoutine != null)
                StopCoroutine(_reloadRoutine);
            RpcPlayReloadAudio(transform.position);
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
            if (
                PlayerManager.Instance == null
                || PlayerManager.Instance.playerRole != PlayerRole.Survivor
            )
                return;

            DamagePopup.Spawn(damagePopupPrefab, worldPos, amount);
        }

        [ClientRpc]
        private void RpcPlayHitVfx(Vector3 worldPos, Vector3 fireDirection)
        {
            HitVfx.Play(hitVfxPrefab, worldPos, fireDirection, hitVfxLifetime);
        }

        [ClientRpc]
        private void RpcPlayImpactVfx(Vector3 worldPos, Vector3 hitNormal)
        {
            HitVfx.PlayImpact(impactVfxPrefab, worldPos, hitNormal, impactVfxLifetime);
        }

        // includeOwner = false: the shooter already played this instantly in TryFire, ahead of the
        // Cmd/Rpc round trip — this only needs to reach everyone else.
        [ClientRpc(includeOwner = false)]
        private void RpcPlayTracer(Vector3 muzzlePosition, Vector3 hitPoint)
        {
            PlayTracer(muzzlePosition, hitPoint);
        }

        private void PlayTracer(Vector3 muzzlePosition, Vector3 hitPoint)
        {
            BulletTracer.Spawn(this, muzzlePosition, hitPoint, tracerSpeed, tracerSegmentLength, tracerWidth, tracerColor);
        }

        [ClientRpc]
        private void RpcPlayShootAudio(Vector3 worldPos)
        {
            AudioManager.Instance?.PlayOneShot(AudioEventIds.PlayerShootSfx, worldPos);
        }

        [ClientRpc]
        private void RpcPlayReloadAudio(Vector3 worldPos)
        {
            AudioManager.Instance?.PlayOneShot(AudioEventIds.PlayerReloadGun, worldPos);
        }

        private void OnAmmoSynced(int oldValue, int newValue)
        {
            OnAmmoChangedEvent?.Invoke(newValue, magazineSize);
        }
    }
}
