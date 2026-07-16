using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    [DisallowMultipleComponent]
    public class SurvivorAbilityController : MonoBehaviour
    {
        [Header("Components")]
        [SerializeField]
        private GameplayPlayer player;

        [SerializeField]
        private PlayerInput input;

        [SerializeField]
        private CameraController cam;

        [Header("Heal Circle")]
        [SerializeField]
        private float healCircleRadius = 4.5f;

        [SerializeField]
        private float healCircleHealPerTick = 12f;

        [SerializeField]
        private float healCircleTickInterval = 1f;

        [SerializeField]
        private int healCircleTickCount = 5;

        [SerializeField]
        private float healCircleCooldown = 0f;

        [SerializeField]
        private GameObject healCircleStartVfxPrefab;

        [Header("Molotov")]
        [SerializeField]
        private MolotovProjectile molotovProjectilePrefab;

        [SerializeField]
        private float molotovThrowStrength = 14f;

        [SerializeField]
        private float molotovUpwardVelocityBonus = 2f;

        [SerializeField]
        private float molotovSpawnHeightOffset = 1.35f;

        [SerializeField]
        private float molotovSpawnForwardOffset = 0.9f;

        [SerializeField]
        private float molotovCooldown = 0f;

        [Header("Steroid")]
        [SerializeField]
        private float steroidSpeedMultiplier = 1.5f;

        [SerializeField]
        private float steroidDuration = 5f;

        [SerializeField]
        private float steroidCooldown = 0f;

        [Header("EMP")]
        [SerializeField]
        private float empRadius = 8f;

        [SerializeField]
        private LayerMask empLayers = Physics.AllLayers;

        [SerializeField]
        private float empCooldown = 0f;

        [SerializeField]
        private GameObject empStartVfxPrefab;

        private readonly Dictionary<SurvivorAbilityType, double> _clientNextReadyTimes = new();
        private readonly Dictionary<SurvivorAbilityType, double> _serverNextReadyTimes = new();

        private void Awake()
        {
            CacheReferences();
        }

        private void OnValidate()
        {
            CacheReferences();
        }

        private void Update()
        {
            if (!CanUseAbilitiesLocally() || !input.AbilityPress)
            {
                return;
            }

            SurvivorAbilityType ability = player.localManager.assignedAbility;
            if (!TryStartLocalCooldown(ability))
            {
                return;
            }

            switch (ability)
            {
                case SurvivorAbilityType.HealCircle:
                    player.CmdActivateHealCircle();
                    break;
                case SurvivorAbilityType.Molotov:
                    player.CmdActivateMolotov(GetLocalMolotovAimDirection());
                    break;
                case SurvivorAbilityType.Steroid:
                    player.CmdActivateSteroid();
                    break;
                case SurvivorAbilityType.Emp:
                    player.CmdActivateEmp();
                    break;
            }
        }

        [Server]
        public void ServerTryActivateHealCircle()
        {
            if (!TryConsumeServerCooldown(SurvivorAbilityType.HealCircle))
            {
                return;
            }

            Vector3 center = player.transform.position;
            player.RpcPlayHealCircleEffect(center, healCircleTickCount * healCircleTickInterval);
            StartCoroutine(ServerHealCircleRoutine(center));
        }

        [Server]
        public void ServerTryActivateMolotov(Vector3 requestedAimDirection)
        {
            if (!TryConsumeServerCooldown(SurvivorAbilityType.Molotov))
            {
                return;
            }

            Vector3 startPoint = GetMolotovSpawnPoint();
            Vector3 initialVelocity = ResolveMolotovInitialVelocity(requestedAimDirection);

            if (!SpawnMolotovProjectile(startPoint, initialVelocity))
            {
                return;
            }
        }

        [Server]
        public void ServerTryActivateSteroid()
        {
            if (!TryConsumeServerCooldown(SurvivorAbilityType.Steroid))
            {
                return;
            }

            player.ServerApplySpeedBoost(steroidSpeedMultiplier, steroidDuration);
        }

        [Server]
        public void ServerTryActivateEmp()
        {
            if (!TryConsumeServerCooldown(SurvivorAbilityType.Emp))
            {
                return;
            }

            Vector3 center = player.transform.position;
            var turrets = new HashSet<DungeonMasterTurret>();
            var c4Traps = new HashSet<C4Trap>();

            foreach (
                Collider hit in Physics.OverlapSphere(
                    center,
                    empRadius,
                    empLayers,
                    QueryTriggerInteraction.Collide
                )
            )
            {
                DungeonMasterTurret turret = hit.GetComponentInParent<DungeonMasterTurret>();
                if (turret != null && turrets.Add(turret))
                {
                    turret.ServerDestroyByEmp();
                    continue;
                }

                C4Trap c4Trap = hit.GetComponentInParent<C4Trap>();
                if (c4Trap != null && c4Traps.Add(c4Trap))
                {
                    c4Trap.ServerDestroyByEmp();
                }
            }

            player.RpcPlayEmpEffect(center, empRadius);
        }

        private void CacheReferences()
        {
            player ??= GetComponent<GameplayPlayer>();
            if (input == null)
            {
                input = transform.root.GetComponentInChildren<PlayerInput>(true);
            }

            if (cam == null)
            {
                cam = transform.root.GetComponentInChildren<CameraController>(true);
            }
        }

        private bool CanUseAbilitiesLocally()
        {
            return player != null
                && player.isLocalPlayer
                && input != null
                && player.localManager != null
                && player.localManager.playerRole == PlayerRole.Survivor
                && !player.IsDungeonMaster
                && !player.IsInactive
                && !player.IsBearTrapped
                && player.health != null
                && player.health.IsAlive;
        }

        [Server]
        private bool CanUseAbilitiesOnServer()
        {
            return player != null
                && player.localManager != null
                && player.localManager.playerRole == PlayerRole.Survivor
                && !player.IsDungeonMaster
                && !player.IsInactive
                && !player.IsBearTrapped
                && player.health != null
                && player.health.IsAlive;
        }

        private bool TryStartLocalCooldown(SurvivorAbilityType abilityType)
        {
            double now = NetworkTime.time;
            if (
                _clientNextReadyTimes.TryGetValue(abilityType, out double readyTime)
                && now < readyTime
            )
            {
                return false;
            }

            _clientNextReadyTimes[abilityType] = now + GetCooldown(abilityType);
            return true;
        }

        [Server]
        private bool TryConsumeServerCooldown(SurvivorAbilityType abilityType)
        {
            if (!CanUseAbilitiesOnServer() || player.localManager.assignedAbility != abilityType)
            {
                return false;
            }

            double now = NetworkTime.time;
            if (
                _serverNextReadyTimes.TryGetValue(abilityType, out double readyTime)
                && now < readyTime
            )
            {
                return false;
            }

            _serverNextReadyTimes[abilityType] = now + GetCooldown(abilityType);
            return true;
        }

        // 1 = just used (full cooldown remaining) -> 0 = ready, i.e. timeLeft / cooldown. Drives the
        // HUD fill so it snaps full on use and drains as the cooldown elapses. Reads the local
        // client's predicted cooldown clock, so it's for the owning survivor's own HUD.
        public float GetCooldownFraction(SurvivorAbilityType abilityType)
        {
            float cooldown = GetCooldown(abilityType);
            if (cooldown <= 0f)
            {
                return 0f;
            }

            double timeLeft = _clientNextReadyTimes.TryGetValue(abilityType, out double readyTime)
                ? readyTime - NetworkTime.time
                : 0d;

            return Mathf.Clamp01((float)(timeLeft / cooldown));
        }

        private float GetCooldown(SurvivorAbilityType abilityType)
        {
            return abilityType switch
            {
                SurvivorAbilityType.HealCircle => healCircleCooldown,
                SurvivorAbilityType.Molotov => molotovCooldown,
                SurvivorAbilityType.Steroid => steroidCooldown,
                SurvivorAbilityType.Emp => empCooldown,
                _ => 0f,
            };
        }

        private Vector3 GetLocalMolotovAimDirection()
        {
            if (cam == null)
            {
                return transform.forward;
            }

            return cam.GetAimDirection();
        }

        [Server]
        private IEnumerator ServerHealCircleRoutine(Vector3 center)
        {
            for (int tick = 0; tick < healCircleTickCount; tick++)
            {
                yield return new WaitForSeconds(healCircleTickInterval);
                ApplyHealCircleTick(center);
            }
        }

        [Server]
        private void ApplyHealCircleTick(Vector3 center)
        {
            var healedPlayers = new HashSet<GameplayPlayer>();
            foreach (
                Collider hit in Physics.OverlapSphere(
                    center,
                    healCircleRadius,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                GameplayPlayer target = hit.GetComponentInParent<GameplayPlayer>();
                if (target == null || !healedPlayers.Add(target))
                {
                    continue;
                }

                if (!IsLivingSurvivor(target))
                {
                    continue;
                }

                target.health.ServerHeal(healCircleHealPerTick);
            }
        }

        [Server]
        private Vector3 ResolveMolotovInitialVelocity(Vector3 requestedAimDirection)
        {
            if (requestedAimDirection.sqrMagnitude <= 0.001f)
            {
                requestedAimDirection = player.transform.forward;
            }

            return requestedAimDirection.normalized * molotovThrowStrength
                + Vector3.up * molotovUpwardVelocityBonus;
        }

        private Vector3 GetMolotovSpawnPoint()
        {
            Vector3 forward = player != null ? player.transform.forward : transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Vector3.forward;
            }

            return transform.position
                + Vector3.up * molotovSpawnHeightOffset
                + forward.normalized * molotovSpawnForwardOffset;
        }

        [Server]
        private bool SpawnMolotovProjectile(Vector3 startPoint, Vector3 initialVelocity)
        {
            if (molotovProjectilePrefab == null)
            {
                Debug.LogWarning(
                    "[SurvivorAbilityController] Molotov projectile prefab is not assigned."
                );
                return false;
            }

            GameObject projectileObject = Instantiate(
                molotovProjectilePrefab.gameObject,
                startPoint,
                Quaternion.identity
            );
            MolotovProjectile projectile = projectileObject.GetComponent<MolotovProjectile>();
            if (projectile == null)
            {
                Debug.LogWarning(
                    "[SurvivorAbilityController] Molotov projectile prefab is missing MolotovProjectile."
                );
                Object.Destroy(projectileObject);
                return false;
            }

            projectile.ServerInitialize(player, initialVelocity);
            NetworkServer.Spawn(projectileObject);

            return true;
        }

        private static bool IsLivingSurvivor(GameplayPlayer target)
        {
            return target != null
                && target.localManager != null
                && target.localManager.playerRole == PlayerRole.Survivor
                && !target.IsDungeonMaster
                && !target.IsInactive
                && target.health != null
                && target.health.IsAlive;
        }

        public void PlayHealCircleStartVfx(Vector3 center, float duration)
        {
            if (healCircleStartVfxPrefab == null)
            {
                return;
            }

            GameObject vfx = Instantiate(healCircleStartVfxPrefab, center, Quaternion.identity);
            if (duration > 0f)
            {
                Destroy(vfx, duration);
            }
        }

        public void PlayEmpStartVfx(Vector3 center, float radius)
        {
            if (empStartVfxPrefab == null)
            {
                return;
            }

            GameObject vfx = Instantiate(empStartVfxPrefab, center, Quaternion.identity);
            Destroy(vfx, 5f);
        }
    }
}
