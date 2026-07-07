using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectRuntime.Actor
{
    public class PlayerVisualAnimator : MonoBehaviour
    {
        private enum VisualState
        {
            Idle,
            Run,
            Jump,
            Death,
        }

        [SerializeField] private GameplayPlayer player;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform normalVisualRoot;
        [SerializeField] private GameObject ghostVisualPrefab;
        [SerializeField] private RuntimeAnimatorController visualController;
        [SerializeField] private string idleStateName = "player_tsuki_idle";
        [SerializeField] private string runStateName = "player_tsuki_run";
        [SerializeField] private string jumpStateName = "player_tsuki_jump";
        [SerializeField] private string deathStateName = "player_tsuki_death";
        [SerializeField] private float runSpeedThreshold = 0.5f;
        [SerializeField] private float runInputThreshold = 0.1f;
        [SerializeField] private float runMovementGraceTime = 0.15f;
        [SerializeField] private float stateBlendDuration = 0.12f;
        [SerializeField] private float deathBlendDuration = 0.05f;
        [SerializeField] private Material auraMaterial;
        [SerializeField] private LayerMask auraOcclusionMask = (1 << 9) | (1 << 10);

        private Vector3 previousPosition;
        private Animator activeAnimator;
        private GameObject ghostVisualRoot;
        private Renderer[] normalRenderers;
        private bool[] normalRendererInitialEnabled;
        private GameObject[] auraObjects;
        private Renderer[] auraRenderers;
        private Renderer[] ghostRenderers;
        private bool[] ghostRendererInitialEnabled;
        private Renderer[] localOwnerHiddenRenderers;
        private VisualState currentState;
        private bool hasVisualState;
        private bool isGhostVisualActive;
        private double movementRunUntil;
        private double jumpVisualUntil;
        private bool wasRunning;

        private void Awake()
        {
            previousPosition = transform.position;
            activeAnimator = animator;
            normalRenderers = normalVisualRoot.GetComponentsInChildren<Renderer>(true);
            normalRendererInitialEnabled = CacheInitialRendererState(normalRenderers);
            CreateAuraOverlays();
            localOwnerHiddenRenderers = normalRenderers;
            ApplyVisualState(VisualState.Idle);
        }

        private void LateUpdate()
        {
            var currentPosition = transform.position;
            ApplyVisualState(ResolveVisualState(currentPosition));
            ApplyLocalOwnerVisibility();
            RefreshAuraVisibility();

            previousPosition = currentPosition;
        }

        public void EnterGhostMode()
        {
            if (!isGhostVisualActive)
            {
                EnsureGhostVisual();
                isGhostVisualActive = true;
                activeAnimator = ghostVisualRoot.GetComponentInChildren<Animator>(true);
                localOwnerHiddenRenderers = ghostRenderers;
                hasVisualState = false;
            }

            SetRendererVisibility(normalRenderers, normalRendererInitialEnabled, false);
            SetAuraObjectsVisible(false);
            SetGhostVisible(false);
            ApplyVisualState(VisualState.Idle);
            ApplyLocalOwnerVisibility();
        }

        public void PlayJump()
        {
            var clipDuration = GetVisualStateAnimationDuration(VisualState.Jump, 0f);
            jumpVisualUntil = Time.timeAsDouble + clipDuration;
            ApplyVisualState(VisualState.Jump);
        }

        public void SetGhostVisible(bool isVisible)
        {
            if (!isGhostVisualActive)
            {
                return;
            }

            SetRendererVisibility(normalRenderers, normalRendererInitialEnabled, false);
            SetRendererVisibility(
                ghostRenderers,
                ghostRendererInitialEnabled,
                isVisible && !player.isLocalPlayer
            );
        }

        private void CreateAuraOverlays()
        {
            auraObjects = new GameObject[normalRenderers.Length];
            auraRenderers = new Renderer[normalRenderers.Length];

            for (var i = 0; i < normalRenderers.Length; i++)
            {
                var source = normalRenderers[i];
                var auraObject = new GameObject($"{source.name}_AuraOverlay")
                {
                    hideFlags = HideFlags.DontSave,
                    layer = source.gameObject.layer,
                };

                var auraTransform = auraObject.transform;
                auraTransform.SetParent(source.transform, false);
                auraTransform.localPosition = Vector3.zero;
                auraTransform.localRotation = Quaternion.identity;
                auraTransform.localScale = Vector3.one;

                auraObjects[i] = auraObject;
                auraRenderers[i] = CreateAuraRenderer(source, auraObject);
                auraObject.SetActive(false);
            }
        }

        private Renderer CreateAuraRenderer(Renderer source, GameObject auraObject)
        {
            Renderer auraRenderer = source switch
            {
                SkinnedMeshRenderer skinnedSource => CreateSkinnedAuraRenderer(
                    skinnedSource,
                    auraObject
                ),
                MeshRenderer meshSource => CreateMeshAuraRenderer(meshSource, auraObject),
                _ => null,
            };

            if (auraRenderer == null)
            {
                return null;
            }

            auraRenderer.sharedMaterials = CreateAuraMaterialSlots(source);
            auraRenderer.shadowCastingMode = ShadowCastingMode.Off;
            auraRenderer.receiveShadows = false;
            auraRenderer.lightProbeUsage = LightProbeUsage.Off;
            auraRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            auraRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            auraRenderer.allowOcclusionWhenDynamic = false;
            return auraRenderer;
        }

        private SkinnedMeshRenderer CreateSkinnedAuraRenderer(
            SkinnedMeshRenderer source,
            GameObject auraObject
        )
        {
            var auraRenderer = auraObject.AddComponent<SkinnedMeshRenderer>();
            auraRenderer.sharedMesh = source.sharedMesh;
            auraRenderer.bones = source.bones;
            auraRenderer.rootBone = source.rootBone;
            auraRenderer.localBounds = source.localBounds;
            auraRenderer.quality = source.quality;
            auraRenderer.updateWhenOffscreen = true;
            return auraRenderer;
        }

        private MeshRenderer CreateMeshAuraRenderer(MeshRenderer source, GameObject auraObject)
        {
            var sourceFilter = source.GetComponent<MeshFilter>();
            var auraFilter = auraObject.AddComponent<MeshFilter>();
            auraFilter.sharedMesh = sourceFilter.sharedMesh;
            return auraObject.AddComponent<MeshRenderer>();
        }

        private Material[] CreateAuraMaterialSlots(Renderer source)
        {
            var slotCount = GetAuraMaterialSlotCount(source);
            var materials = new Material[slotCount];
            for (var i = 0; i < materials.Length; i++)
            {
                materials[i] = auraMaterial;
            }

            return materials;
        }

        private static int GetAuraMaterialSlotCount(Renderer source)
        {
            var slotCount = source.sharedMaterials.Length;
            if (source is SkinnedMeshRenderer skinnedSource && skinnedSource.sharedMesh != null)
            {
                slotCount = Mathf.Max(slotCount, skinnedSource.sharedMesh.subMeshCount);
            }
            else if (source is MeshRenderer)
            {
                var meshFilter = source.GetComponent<MeshFilter>();
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    slotCount = Mathf.Max(slotCount, meshFilter.sharedMesh.subMeshCount);
                }
            }

            return Mathf.Max(1, slotCount);
        }

        private void RefreshAuraVisibility()
        {
            SetAuraObjectsVisible(ShouldShowAura());
        }

        private bool ShouldShowAura()
        {
            var viewer = PlayerManager.Instance;
            if (viewer == null || player.localManager == viewer)
            {
                return false;
            }

            if (!IsAuraTarget())
            {
                return false;
            }

            if (!CanViewerSeeAura(viewer))
            {
                return false;
            }

            var worldCamera = Camera.main;
            if (worldCamera == null)
            {
                return false;
            }

            return Physics.Linecast(
                worldCamera.transform.position,
                GetAuraTargetPosition(),
                auraOcclusionMask,
                QueryTriggerInteraction.Ignore
            );
        }

        private bool IsAuraTarget()
        {
            return player.localManager.playerRole == PlayerRole.Survivor
                && player.localManager.lives > 0
                && !player.IsGhost
                && (!player.IsInactive || player.IsDowned);
        }

        private static bool CanViewerSeeAura(PlayerManager viewer)
        {
            if (viewer.playerRole == PlayerRole.DungeonMaster)
            {
                return true;
            }

            return viewer.playerRole == PlayerRole.Survivor
                && viewer.lives > 0
                && viewer.player != null
                && !viewer.player.IsGhost
                && (!viewer.player.IsInactive || viewer.player.IsDowned);
        }

        private Vector3 GetAuraTargetPosition()
        {
            var hasBounds = false;
            var bounds = new Bounds();
            for (var i = 0; i < normalRenderers.Length; i++)
            {
                if (!normalRendererInitialEnabled[i])
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = normalRenderers[i].bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(normalRenderers[i].bounds);
            }

            return hasBounds ? bounds.center : normalVisualRoot.position;
        }

        private void SetAuraObjectsVisible(bool isVisible)
        {
            if (auraObjects == null)
            {
                return;
            }

            for (var i = 0; i < auraObjects.Length; i++)
            {
                if (auraObjects[i] == null || auraRenderers[i] == null)
                {
                    continue;
                }

                auraObjects[i].SetActive(isVisible && normalRendererInitialEnabled[i]);
            }
        }

        private VisualState ResolveVisualState(Vector3 currentPosition)
        {
            if (player.IsDowned || (player.IsDead && !player.IsGhost))
            {
                return VisualState.Death;
            }

            var movementDelta = currentPosition - previousPosition;
            if (
                Time.timeAsDouble < jumpVisualUntil
                || (currentState == VisualState.Jump && !player.groundCheck.IsGrounded)
            )
            {
                return VisualState.Jump;
            }

            var inputSqrMagnitude = player.input.MoveVector.sqrMagnitude;
            movementDelta.y = 0f;
            var horizontalSpeed =
                Time.deltaTime > 0f ? movementDelta.magnitude / Time.deltaTime : 0f;

            var runStartSpeedThreshold = runSpeedThreshold;
            var runStopSpeedThreshold = runSpeedThreshold * 0.5f;
            var runStartInputThreshold = runInputThreshold * runInputThreshold;
            var runStopInputThreshold = runStartInputThreshold * 0.25f;
            if (horizontalSpeed > (wasRunning ? runStopSpeedThreshold : runStartSpeedThreshold))
            {
                movementRunUntil = Time.timeAsDouble + runMovementGraceTime;
            }

            var inputRunning = wasRunning
                ? inputSqrMagnitude > runStopInputThreshold
                : inputSqrMagnitude > runStartInputThreshold;
            var movementRunning = Time.timeAsDouble < movementRunUntil;
            wasRunning = inputRunning || movementRunning;

            return wasRunning ? VisualState.Run : VisualState.Idle;
        }

        private void ApplyVisualState(VisualState state)
        {
            if (
                hasVisualState
                && currentState == state
                && activeAnimator.runtimeAnimatorController == visualController
            )
            {
                return;
            }

            var isFirstApply = !hasVisualState;
            hasVisualState = true;
            currentState = state;
            activeAnimator.speed = 1f;

            var controllerChanged = activeAnimator.runtimeAnimatorController != visualController;
            if (controllerChanged)
            {
                activeAnimator.runtimeAnimatorController = visualController;
                activeAnimator.Rebind();
            }

            var stateName = GetVisualStateName(state);
            if (isFirstApply || controllerChanged)
            {
                // Nothing meaningful to blend from on the first pose or right after a
                // controller swap (ghost mode) — snap instantly.
                activeAnimator.Play(stateName, 0, 0f);
                activeAnimator.Update(0f);
            }
            else
            {
                activeAnimator.CrossFadeInFixedTime(
                    stateName,
                    GetVisualStateBlendDuration(state),
                    0,
                    0f
                );
            }
        }

        private float GetVisualStateBlendDuration(VisualState state)
        {
            return state switch
            {
                VisualState.Death => deathBlendDuration,
                _ => stateBlendDuration,
            };
        }

        public void ApplyDeathPose(Animator targetAnimator)
        {
            targetAnimator.speed = 1f;
            targetAnimator.runtimeAnimatorController = visualController;
            targetAnimator.Rebind();
            targetAnimator.Play(deathStateName, 0, 1f);
            targetAnimator.Update(0f);
            targetAnimator.speed = 0f;
        }

        public float GetDeathAnimationDuration(float fallbackDuration)
        {
            return GetVisualStateAnimationDuration(VisualState.Death, fallbackDuration);
        }

        private float GetVisualStateAnimationDuration(VisualState state, float fallbackDuration)
        {
            var stateName = GetVisualStateName(state);
            foreach (var clip in visualController.animationClips)
            {
                if (clip != null && clip.name == stateName)
                {
                    return clip.length;
                }
            }

            return Mathf.Max(0f, fallbackDuration);
        }

        private string GetVisualStateName(VisualState state)
        {
            return state switch
            {
                VisualState.Run => runStateName,
                VisualState.Jump => jumpStateName,
                VisualState.Death => deathStateName,
                _ => idleStateName,
            };
        }

        private void ApplyLocalOwnerVisibility()
        {
            if (!player.isLocalPlayer)
            {
                return;
            }

            SetRendererVisibility(localOwnerHiddenRenderers, null, false);
        }

        private void EnsureGhostVisual()
        {
            if (ghostVisualRoot != null)
            {
                return;
            }

            ghostVisualRoot = Instantiate(ghostVisualPrefab, normalVisualRoot.parent);
            ghostVisualRoot.name = ghostVisualPrefab.name;

            var ghostTransform = ghostVisualRoot.transform;
            ghostTransform.localPosition = normalVisualRoot.localPosition;
            ghostTransform.localRotation = normalVisualRoot.localRotation;
            ghostTransform.localScale = normalVisualRoot.localScale;

            foreach (var ghostCollider in ghostVisualRoot.GetComponentsInChildren<Collider>(true))
            {
                ghostCollider.enabled = false;
            }

            ghostRenderers = ghostVisualRoot.GetComponentsInChildren<Renderer>(true);
            ghostRendererInitialEnabled = CacheInitialRendererState(ghostRenderers);
        }

        private static bool[] CacheInitialRendererState(Renderer[] renderers)
        {
            var initialState = new bool[renderers.Length];
            for (var i = 0; i < renderers.Length; i++)
            {
                initialState[i] = renderers[i].enabled;
            }

            return initialState;
        }

        private static void SetRendererVisibility(
            Renderer[] renderers,
            bool[] initialEnabled,
            bool isVisible
        )
        {
            for (var i = 0; i < renderers.Length; i++)
            {
                renderers[i].enabled =
                    isVisible && (initialEnabled == null || initialEnabled[i]);
            }
        }
    }
}
