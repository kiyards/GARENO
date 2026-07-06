using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class PlayerVisualAnimator : MonoBehaviour
    {
        private enum VisualState
        {
            Idle,
            Run,
            Death,
        }

        private static readonly int MixamoStateHash = Animator.StringToHash("Base Layer.mixamo_com");

        [SerializeField] private GameplayPlayer player;
        [SerializeField] private Animator animator;
        [SerializeField] private Transform normalVisualRoot;
        [SerializeField] private GameObject ghostVisualPrefab;
        [SerializeField] private RuntimeAnimatorController idleController;
        [SerializeField] private RuntimeAnimatorController runController;
        [SerializeField] private RuntimeAnimatorController deathController;
        [SerializeField] private float runSpeedThreshold = 0.5f;
        [SerializeField] private float runInputThreshold = 0.1f;
        [SerializeField] private float runMovementGraceTime = 0.15f;

        private Vector3 previousPosition;
        private Animator activeAnimator;
        private GameObject ghostVisualRoot;
        private Renderer[] normalRenderers;
        private bool[] normalRendererInitialEnabled;
        private Renderer[] ghostRenderers;
        private bool[] ghostRendererInitialEnabled;
        private Renderer[] localOwnerHiddenRenderers;
        private VisualState currentState;
        private bool hasVisualState;
        private bool isGhostVisualActive;
        private double movementRunUntil;
        private bool wasRunning;

        private void Awake()
        {
            previousPosition = transform.position;
            activeAnimator = animator;
            normalRenderers = normalVisualRoot.GetComponentsInChildren<Renderer>(true);
            normalRendererInitialEnabled = CacheInitialRendererState(normalRenderers);
            localOwnerHiddenRenderers = normalRenderers;
            ApplyVisualState(VisualState.Idle);
        }

        private void LateUpdate()
        {
            var currentPosition = transform.position;
            ApplyVisualState(ResolveVisualState(currentPosition));
            ApplyLocalOwnerVisibility();

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
            SetGhostVisible(false);
            ApplyVisualState(VisualState.Idle);
            ApplyLocalOwnerVisibility();
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

        private VisualState ResolveVisualState(Vector3 currentPosition)
        {
            if (player.IsDowned || (player.IsDead && !player.IsGhost))
            {
                return VisualState.Death;
            }

            var movementDelta = currentPosition - previousPosition;
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
            var controller = GetVisualStateController(state);
            if (
                hasVisualState
                && currentState == state
                && activeAnimator.runtimeAnimatorController == controller
            )
            {
                return;
            }

            hasVisualState = true;
            currentState = state;
            activeAnimator.speed = 1f;
            activeAnimator.runtimeAnimatorController = controller;
            activeAnimator.Rebind();
            activeAnimator.Update(0f);
        }

        public void ApplyDeathPose(Animator targetAnimator)
        {
            targetAnimator.speed = 1f;
            targetAnimator.runtimeAnimatorController = deathController;
            targetAnimator.Rebind();
            targetAnimator.Play(MixamoStateHash, 0, 1f);
            targetAnimator.Update(0f);
            targetAnimator.speed = 0f;
        }

        public float GetDeathAnimationDuration(float fallbackDuration)
        {
            return GetVisualStateAnimationDuration(VisualState.Death, fallbackDuration);
        }

        private float GetVisualStateAnimationDuration(VisualState state, float fallbackDuration)
        {
            var controller = GetVisualStateController(state);
            var duration = 0f;
            foreach (var clip in controller.animationClips)
            {
                duration = Mathf.Max(duration, clip.length);
            }

            return duration > 0f ? duration : Mathf.Max(0f, fallbackDuration);
        }

        private RuntimeAnimatorController GetVisualStateController(VisualState state)
        {
            return state switch
            {
                VisualState.Run => runController,
                VisualState.Death => deathController,
                _ => idleController,
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
