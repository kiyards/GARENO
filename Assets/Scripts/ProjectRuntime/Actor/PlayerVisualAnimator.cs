using Mirror;
using ProjectRuntime.Network;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectRuntime.Actor
{
    [DefaultExecutionOrder(-2)]
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
        [SerializeField] private GameObject defaultModelPrefab;
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
        private bool wasRunning;
        private NetworkAnimator networkAnimator;
        private GameObject activeModelPrefab;

        public Material AuraMaterial => auraMaterial;

        private void Awake()
        {
            previousPosition = transform.position;
            activeModelPrefab = defaultModelPrefab;
            InitializeVisualCaches();
            ApplyVisualState(VisualState.Idle);
        }

        // Caches everything derived from the current visual model (renderers, aura overlays, the
        // network-animator binding). Runs at Awake and again after ApplyCharacterModel swaps the model.
        private void InitializeVisualCaches()
        {
            activeAnimator = animator;
            normalRenderers = normalVisualRoot.GetComponentsInChildren<Renderer>(true);
            normalRendererInitialEnabled = CacheInitialRendererState(normalRenderers);
            CreateAuraOverlays();
            localOwnerHiddenRenderers = normalRenderers;
            EnsureNetworkAnimator();
        }

        // True when <paramref name="definition"/> would actually change the rendered model. The
        // baked baseline (Tsuki, i.e. the Steroid mapping) resolves to activeModelPrefab and is a no-op.
        public bool WillSwapTo(CharacterModelDefinition definition)
        {
            return definition != null
                && definition.ModelPrefab != null
                && definition.ModelPrefab != activeModelPrefab;
        }

        // Replaces the character model at runtime and rebinds every model-derived cache so the new
        // model animates through the exact same path as the baseline, and is structured identically
        // in the hierarchy (only the assets differ). No-op when the model is unchanged; returns the
        // collider re-created on the new visual root (or null if the baseline visual had none) so the
        // owner can repoint its collider reference.
        public Collider ApplyCharacterModel(CharacterModelDefinition definition)
        {
            if (!WillSwapTo(definition))
            {
                return null;
            }

            var oldVisualRoot = normalVisualRoot;
            var visualParent = oldVisualRoot.parent;

            var newVisual = Instantiate(definition.ModelPrefab, visualParent);
            var newVisualTransform = newVisual.transform;
            newVisualTransform.localPosition = oldVisualRoot.localPosition;
            newVisualTransform.localRotation = oldVisualRoot.localRotation;
            newVisualTransform.localScale = oldVisualRoot.localScale;
            // Match only the root layer (as the baked visual does); the mesh children keep their
            // imported layer so camera culling behaves exactly like the baseline model.
            newVisual.layer = oldVisualRoot.gameObject.layer;

            // Re-create the collider at the same hierarchy level as the baseline — on the visual root
            // itself, not the parent body — so the running instance matches the baseline structure.
            var swappedCollider = ReplicateVisualRootCollider(oldVisualRoot.gameObject, newVisual);

            // The aura overlays and ghost clone are bound to the outgoing model — release them before
            // we drop our references so nothing points at a destroyed object.
            DestroyAuraOverlays();
            DestroyGhostVisual();

            normalVisualRoot = newVisualTransform;
            animator = newVisual.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                animator = newVisual.AddComponent<Animator>();
            }

            visualController = definition.AnimatorController;
            idleStateName = definition.IdleStateName;
            runStateName = definition.RunStateName;
            jumpStateName = definition.JumpStateName;
            deathStateName = definition.DeathStateName;
            ghostVisualPrefab = definition.GhostPrefab;

            isGhostVisualActive = false;
            hasVisualState = false;
            InitializeVisualCaches();
            ApplyVisualState(VisualState.Idle);
            ReinitializeNetworkAnimator();

            activeModelPrefab = definition.ModelPrefab;
            Destroy(oldVisualRoot.gameObject);
            return swappedCollider;
        }

        // Clones the baseline visual root's CapsuleCollider onto the new visual root so every model is
        // set up with its collider at the same level, with identical parameters. Returns null when the
        // baseline visual has no capsule collider.
        private static Collider ReplicateVisualRootCollider(GameObject oldRoot, GameObject newRoot)
        {
            var source = oldRoot.GetComponent<CapsuleCollider>();
            if (source == null)
            {
                return null;
            }

            var clone = newRoot.AddComponent<CapsuleCollider>();
            clone.center = source.center;
            clone.radius = source.radius;
            clone.height = source.height;
            clone.direction = source.direction;
            clone.isTrigger = source.isTrigger;
            clone.sharedMaterial = source.sharedMaterial;
            clone.contactOffset = source.contactOffset;
            clone.enabled = source.enabled;
            return clone;
        }

        private void LateUpdate()
        {
            var currentPosition = transform.position;
            if (ShouldDriveVisualState())
            {
                ApplyVisualState(ResolveVisualState(currentPosition));
            }

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
            if (!ShouldDriveVisualState())
            {
                return;
            }

            ApplyVisualState(VisualState.Jump);
        }

        private void EnsureNetworkAnimator()
        {
            networkAnimator ??= GetComponent<NetworkAnimator>();
            networkAnimator.animator = animator;
            networkAnimator.clientAuthority = true;
            networkAnimator.syncDirection = SyncDirection.ClientToServer;
        }

        private void ReinitializeNetworkAnimator()
        {
            if (networkAnimator == null)
            {
                return;
            }

            // NetworkAnimator caches its parameter/layer arrays against the animator in OnEnable and
            // never re-reads them when the field is reassigned, so bounce enabled to rebind cleanly.
            networkAnimator.animator = animator;
            if (networkAnimator.isActiveAndEnabled)
            {
                networkAnimator.enabled = false;
                networkAnimator.enabled = true;
            }
        }

        private void DestroyAuraOverlays()
        {
            if (auraObjects != null)
            {
                for (var i = 0; i < auraObjects.Length; i++)
                {
                    if (auraObjects[i] != null)
                    {
                        Destroy(auraObjects[i]);
                    }
                }
            }

            auraObjects = null;
            auraRenderers = null;
        }

        private void DestroyGhostVisual()
        {
            if (ghostVisualRoot != null)
            {
                Destroy(ghostVisualRoot);
                ghostVisualRoot = null;
            }

            ghostRenderers = null;
            ghostRendererInitialEnabled = null;
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
                && (!player.IsInactive || player.IsDowned);
        }

        private static bool CanViewerSeeAura(PlayerManager viewer)
        {
            if (viewer.playerRole == PlayerRole.DungeonMaster)
            {
                return true;
            }

            return viewer.playerRole == PlayerRole.Survivor
                && viewer.player != null
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
            if (player.IsDowned)
            {
                return VisualState.Death;
            }

            var movementDelta = currentPosition - previousPosition;
            if (currentState == VisualState.Jump && IsOneShotVisualStateStillPlaying(VisualState.Jump))
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

        private bool ShouldDriveVisualState()
        {
            if (isGhostVisualActive)
            {
                return true;
            }

            return !NetworkClient.active || player.isLocalPlayer;
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

        private bool IsOneShotVisualStateStillPlaying(VisualState state)
        {
            if (
                activeAnimator == null
                || !activeAnimator.enabled
                || activeAnimator.layerCount <= 0
            )
            {
                return false;
            }

            var expectedStateName = GetVisualStateName(state);
            var currentStateInfo = activeAnimator.GetCurrentAnimatorStateInfo(0);
            if (
                activeAnimator.IsInTransition(0)
                && activeAnimator.GetNextAnimatorStateInfo(0).IsName(expectedStateName)
            )
            {
                return true;
            }

            return currentStateInfo.IsName(expectedStateName)
                && (currentStateInfo.loop || currentStateInfo.normalizedTime < 0.98f);
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
