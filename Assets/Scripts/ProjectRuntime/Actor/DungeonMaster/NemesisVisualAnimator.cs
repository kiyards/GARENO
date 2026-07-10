using Mirror;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    // Visual/animation layer for the Nemesis. Modelled on PlayerVisualAnimator: the Nemesis is
    // owner-authoritative (the possessing Dungeon Master owns its NetworkTransform), so the owner
    // drives the animator by name (Play / CrossFadeInFixedTime) and the prefab's NetworkAnimator
    // (clientAuthority, ClientToServer) replicates those state changes to every other peer.
    //
    // Idle/walk are derived from the owner's positional delta; punch/ground-slam/lunge swings are
    // fired from DungeonMasterNemesis.OnAttackStartedEvent (which fires as the action window opens, so
    // the animation lines up with the server's impact-frame damage); death plays on disassemble.
    [DefaultExecutionOrder(-2)]
    [DisallowMultipleComponent]
    public class NemesisVisualAnimator : MonoBehaviour
    {
        private enum VisualState
        {
            Spawn,
            Idle,
            Walk,
            Punch,
            Lunge,
            GroundSlam,
            Death,
        }

        [SerializeField] private DungeonMasterNemesis nemesis;

        // Optional: resolved from the model child at runtime when left unassigned.
        [SerializeField] private Animator animator;
        [SerializeField] private RuntimeAnimatorController visualController;

        [SerializeField] private string spawnStateName = "enemy_nemesis_spawn";
        [SerializeField] private string idleStateName = "enemy_nemesis_idle";
        [SerializeField] private string walkStateName = "enemy_nemesis_walk";
        [SerializeField] private string punchStateName = "enemy_nemesis_punch";
        [SerializeField] private string lungeStateName = "enemy_nemesis_lunge";
        [SerializeField] private string groundSlamStateName = "enemy_nemesis_groundslam";
        [SerializeField] private string deathStateName = "enemy_nemesis_death";

        [SerializeField] private float walkSpeedThreshold = 0.5f;
        [SerializeField] private float walkMovementGraceTime = 0.15f;
        [SerializeField] private float locomotionBlendDuration = 0.12f;
        [SerializeField] private float actionBlendDuration = 0.05f;

        private Vector3 previousPosition;
        private VisualState currentState;
        private bool hasVisualState;
        private double walkUntil;
        private double attackVisualUntil;
        private bool deathPlayed;
        private NetworkAnimator networkAnimator;

        private void Awake()
        {
            previousPosition = transform.position;

            // Bind the controller on every peer (including non-owners) so the NetworkAnimator's
            // synced state hashes resolve - non-owners never call ApplyVisualState again after this.
            ApplyVisualState(VisualState.Idle);
            EnsureNetworkAnimator();
        }

        private void OnEnable()
        {
            nemesis.OnAttackStartedEvent += HandleAttackStarted;
        }

        private void OnDisable()
        {
            nemesis.OnAttackStartedEvent -= HandleAttackStarted;
        }

        private void LateUpdate()
        {
            var currentPosition = transform.position;

            if (ShouldDriveVisualState())
            {
                if (nemesis.IsDisassembling)
                {
                    if (!deathPlayed)
                    {
                        deathPlayed = true;
                        ApplyVisualState(VisualState.Death);
                    }
                }
                else if (nemesis.IsSpawning)
                {
                    // Materializing - hold the spawn animation; movement/attacks are locked out until it
                    // ends (see DungeonMasterNemesis.IsSpawning), so nothing else competes here.
                    ApplyVisualState(VisualState.Spawn);
                }
                else if (Time.timeAsDouble >= attackVisualUntil)
                {
                    // Not mid-swing - resolve locomotion. While within the attack window the current
                    // attack state is simply held.
                    ApplyVisualState(ResolveLocomotion(currentPosition));
                }

                TickLoopingVisualState();
            }

            previousPosition = currentPosition;
        }

        private void HandleAttackStarted(NemesisAttackType type)
        {
            if (!ShouldDriveVisualState() || nemesis.IsDisassembling)
            {
                return;
            }

            var state = GetAttackVisualState(type);
            var stateName = GetVisualStateName(state);
            if (string.IsNullOrEmpty(stateName))
            {
                // Missing clip binding on this prefab/controller; keep locomotion rather than
                // interrupting the dash with an invalid state name.
                return;
            }

            attackVisualUntil = Time.timeAsDouble + GetVisualStateAnimationDuration(state);
            ApplyVisualState(state);
        }

        private VisualState ResolveLocomotion(Vector3 currentPosition)
        {
            var movementDelta = currentPosition - previousPosition;
            movementDelta.y = 0f;
            var speed = Time.deltaTime > 0f ? movementDelta.magnitude / Time.deltaTime : 0f;
            if (speed > walkSpeedThreshold)
            {
                walkUntil = Time.timeAsDouble + walkMovementGraceTime;
            }

            return Time.timeAsDouble < walkUntil ? VisualState.Walk : VisualState.Idle;
        }

        private bool ShouldDriveVisualState()
        {
            return !NetworkClient.active || nemesis.isOwned;
        }

        private void EnsureNetworkAnimator()
        {
            networkAnimator ??= GetComponent<NetworkAnimator>();
            networkAnimator.animator = animator;
            networkAnimator.clientAuthority = true;
            networkAnimator.syncDirection = SyncDirection.ClientToServer;
        }

        private void ApplyVisualState(VisualState state)
        {
            if (animator == null)
            {
                return;
            }

            if (
                hasVisualState
                && currentState == state
                && animator.runtimeAnimatorController == visualController
            )
            {
                return;
            }

            var isFirstApply = !hasVisualState;
            hasVisualState = true;
            currentState = state;
            animator.speed = 1f;

            var controllerChanged = animator.runtimeAnimatorController != visualController;
            if (controllerChanged)
            {
                animator.runtimeAnimatorController = visualController;
                animator.Rebind();
            }

            var stateName = GetVisualStateName(state);
            if (isFirstApply || controllerChanged)
            {
                // Nothing meaningful to blend from on the first pose or right after a controller
                // (re)bind - snap instantly.
                animator.Play(stateName, 0, 0f);
                animator.Update(0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(stateName, GetVisualStateBlendDuration(state), 0, 0f);
            }
        }

        // Safety net (matches ZombieEnemy): if a looping locomotion clip is ever imported without
        // loopTime set, the driving peer restarts it at the end so it doesn't freeze on the last frame.
        // Idle/walk are imported as looping, so this is normally a no-op (stateInfo.loop short-circuits).
        private void TickLoopingVisualState()
        {
            if (
                animator == null
                || !animator.enabled
                || animator.layerCount <= 0
                || !IsLoopingVisualState(currentState)
            )
            {
                return;
            }

            var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.loop || stateInfo.normalizedTime < 0.98f)
            {
                return;
            }

            animator.Play(GetVisualStateName(currentState), 0, 0f);
            animator.Update(0f);
        }

        private static bool IsLoopingVisualState(VisualState state)
        {
            return state == VisualState.Idle || state == VisualState.Walk;
        }

        private float GetVisualStateBlendDuration(VisualState state)
        {
            return state switch
            {
                VisualState.Idle => locomotionBlendDuration,
                VisualState.Walk => locomotionBlendDuration,
                _ => actionBlendDuration,
            };
        }

        // Length of the death clip, so the server can hold the Nemesis on screen until the death
        // animation has played out before despawning. Mirrors PlayerVisualAnimator.GetDeathAnimationDuration.
        public float GetDeathAnimationDuration(float fallbackDuration)
        {
            var duration = GetVisualStateAnimationDuration(VisualState.Death);
            return duration > 0f ? duration : fallbackDuration;
        }

        private float GetVisualStateAnimationDuration(VisualState state)
        {
            var stateName = GetVisualStateName(state);
            foreach (var clip in visualController.animationClips)
            {
                if (clip != null && clip.name == stateName)
                {
                    return clip.length;
                }
            }

            return 0f;
        }

        private static VisualState GetAttackVisualState(NemesisAttackType type)
        {
            return type switch
            {
                NemesisAttackType.Punch => VisualState.Punch,
                NemesisAttackType.Lunge => VisualState.Lunge,
                NemesisAttackType.GroundSlam => VisualState.GroundSlam,
                _ => VisualState.Idle,
            };
        }

        private string GetVisualStateName(VisualState state)
        {
            return state switch
            {
                VisualState.Spawn => spawnStateName,
                VisualState.Walk => walkStateName,
                VisualState.Punch => punchStateName,
                VisualState.Lunge => lungeStateName,
                VisualState.GroundSlam => groundSlamStateName,
                VisualState.Death => deathStateName,
                _ => idleStateName,
            };
        }
    }
}
