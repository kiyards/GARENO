using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Combat;
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

        [Header("Molotov")]
        [SerializeField]
        private MolotovProjectile molotovProjectilePrefab;

        [SerializeField]
        private float molotovRange = 16f;

        [SerializeField]
        private float molotovThrowSpeed = 14f;

        [SerializeField]
        private float molotovUpwardVelocity = 4f;

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
                    player.CmdActivateMolotov(GetLocalMolotovTargetPoint());
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
            player.RpcPlayHealCircleEffect(
                center,
                healCircleRadius,
                healCircleTickCount,
                healCircleTickInterval
            );
            StartCoroutine(ServerHealCircleRoutine(center));
        }

        [Server]
        public void ServerTryActivateMolotov(Vector3 requestedTargetPoint)
        {
            if (!TryConsumeServerCooldown(SurvivorAbilityType.Molotov))
            {
                return;
            }

            Vector3 startPoint = GetMolotovSpawnPoint();
            Vector3 initialVelocity = ResolveMolotovInitialVelocity(
                startPoint,
                requestedTargetPoint
            );

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
            player.RpcPlaySteroidEffect(player.netId, steroidDuration);
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
            if (
                !CanUseAbilitiesOnServer()
                || player.localManager.assignedAbility != abilityType
            )
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

        private Vector3 GetLocalMolotovTargetPoint()
        {
            if (cam == null)
            {
                return transform.position + transform.forward * molotovRange;
            }

            bool occluded = cam.GetAimData(
                molotovRange,
                out Vector3 origin,
                out Vector3 direction,
                out RaycastHit occlusionHit
            );

            List<RaycastData> hits = cam.GetRaycastData(molotovRange);
            if (hits.Count > 0)
            {
                return hits[0].hitPoint;
            }

            if (occluded)
            {
                return occlusionHit.point;
            }

            return origin + direction * molotovRange;
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
        private Vector3 ResolveMolotovInitialVelocity(
            Vector3 startPoint,
            Vector3 requestedTargetPoint
        )
        {
            Vector3 requestDirection = requestedTargetPoint - startPoint;
            if (requestDirection.sqrMagnitude <= 0.001f)
            {
                requestDirection = player.transform.forward;
            }

            Vector3 clampedOffset = Vector3.ClampMagnitude(requestDirection, molotovRange);
            Vector3 horizontalVelocity =
                Vector3.ProjectOnPlane(clampedOffset.normalized, Vector3.up) * molotovThrowSpeed;
            if (horizontalVelocity.sqrMagnitude <= 0.001f)
            {
                horizontalVelocity = player.transform.forward * molotovThrowSpeed;
            }

            return horizontalVelocity + Vector3.up * molotovUpwardVelocity;
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
    }

    public static class SurvivorAbilityVfx
    {
        public static void SpawnHealCircle(Vector3 center, float radius, int pulses, float interval)
        {
            SpawnPulseSeries(
                center,
                radius,
                pulses,
                interval,
                new Color(0.35f, 0.95f, 0.55f, 0.35f)
            );
        }

        public static void SpawnEmp(Vector3 center, float radius)
        {
            SpawnSinglePulse(center, radius, new Color(0.35f, 0.85f, 1f, 0.3f), 0.4f, 0.15f);
        }

        public static void SpawnSteroidAura(Transform target, float duration)
        {
            if (target == null)
            {
                return;
            }

            GameObject aura = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            aura.name = "SteroidAuraVfx";
            aura.transform.SetParent(target, false);
            aura.transform.localPosition = Vector3.zero;
            aura.transform.localScale = new Vector3(0.85f, 0.02f, 0.85f);

            if (aura.TryGetComponent(out Collider collider))
            {
                Object.Destroy(collider);
            }

            ApplyRendererColor(aura, new Color(1f, 0.92f, 0.2f, 0.42f));
            Object.Destroy(aura, duration);
        }

        private static void SpawnPulseSeries(
            Vector3 center,
            float radius,
            int pulses,
            float interval,
            Color color
        )
        {
            SimpleAbilityVfxRunner runner = new GameObject(
                "AbilityPulseRunner"
            ).AddComponent<SimpleAbilityVfxRunner>();
            runner.RunPulses(center, radius, pulses, interval, color);
        }

        private static void SpawnSinglePulse(
            Vector3 center,
            float radius,
            Color color,
            float lifetime,
            float yScale
        )
        {
            GameObject pulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulse.name = "AbilityPulseVfx";
            pulse.transform.position = center;
            pulse.transform.localScale = new Vector3(radius * 2f, yScale, radius * 2f);

            if (pulse.TryGetComponent(out Collider collider))
            {
                Object.Destroy(collider);
            }

            ApplyRendererColor(pulse, color);
            Object.Destroy(pulse, lifetime);
        }

        private static void ApplyRendererColor(GameObject target, Color color)
        {
            if (!target.TryGetComponent(out Renderer renderer))
            {
                return;
            }

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.material.color = color;
        }
    }

    public class SimpleAbilityVfxRunner : MonoBehaviour
    {
        public void RunPulses(Vector3 center, float radius, int pulses, float interval, Color color)
        {
            StartCoroutine(PulseRoutine(center, radius, pulses, interval, color));
        }

        private IEnumerator PulseRoutine(
            Vector3 center,
            float radius,
            int pulses,
            float interval,
            Color color
        )
        {
            for (int pulseIndex = 0; pulseIndex < pulses; pulseIndex++)
            {
                SpawnPulse(center, radius, color);
                yield return new WaitForSeconds(interval);
            }

            Object.Destroy(gameObject);
        }

        private static void SpawnPulse(Vector3 center, float radius, Color color)
        {
            GameObject pulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pulse.name = "AbilityFieldVfx";
            pulse.transform.position = center;
            pulse.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

            if (pulse.TryGetComponent(out Collider collider))
            {
                Object.Destroy(collider);
            }

            if (pulse.TryGetComponent(out Renderer renderer))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.material.color = color;
            }

            Object.Destroy(pulse, 0.55f);
        }
    }
}
