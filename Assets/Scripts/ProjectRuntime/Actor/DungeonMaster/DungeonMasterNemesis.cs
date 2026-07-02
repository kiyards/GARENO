using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public enum NemesisStatus
    {
        Active,
        Disassembling,
    }

    public enum NemesisAttackType
    {
        Punch,
        Lunge,
        GroundSlam,
    }

    // The controllable Nemesis entity. Spawned by DungeonMasterNemesisController with the Dungeon
    // Master's connection as owner, so that client is authoritative over its NetworkTransform and
    // drives its movement directly — mirroring the client-authoritative survivor/DM movement model.
    // Invulnerable by design (no Health/IDamageable), so survivor shots pass through without effect.
    // A server-side lifetime coroutine tears it down after `lifetime` seconds; its destruction returns
    // the Dungeon Master to top-down placement (see DungeonMasterNemesisController.DetachSpawnedNemesis).
    // Modelled on DungeonMasterTurret, minus ammo/firing/assembly.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    public class DungeonMasterNemesis : NetworkBehaviour
    {
        [Header("Visuals")]
        [SerializeField]
        private Transform nemesisRoot;

        [Header("Movement")]
        // Speed as a multiple of the possessing player's survivor moveSpeed.
        // Resolved from the owner at runtime so it tracks the real survivor speed rather than a
        // hardcoded value that drifts when the designer retunes movement.
        [SerializeField]
        private float speedMultiplier = 1.1f;

        // Fallback survivor base speed, used only if the owner can't be resolved while moving.
        [SerializeField]
        private float fallbackBaseSpeed = 8f;

        [SerializeField]
        private float turnSpeed = 720f;

        [Header("Lifetime")]
        [SerializeField]
        private float lifetime = 60f;

        [SerializeField]
        private float disassemblyDuration = 0.5f;

        [Header("Punch")]
        [SerializeField]
        private float punchDamage = 80f;

        [SerializeField]
        private float punchRadius = 2.5f;

        [SerializeField]
        private float punchCooldown = 1f;

        [SerializeField]
        private float punchActionDuration = 0.3f;

        [Header("Lunge")]
        [SerializeField]
        private float lungeDamage = 50f;

        [SerializeField]
        private float lungeDistance = 6f;

        [SerializeField]
        private float lungeRadius = 1.5f;

        [SerializeField]
        private float lungeKnockbackForce = 8f;

        [SerializeField]
        private float lungeCooldown = 3f;

        [SerializeField]
        private float lungeActionDuration = 0.4f;

        [Header("Ground Slam")]
        [SerializeField]
        private float groundSlamDamage = 50f;

        [SerializeField]
        private float groundSlamRadius = 5f;

        [SerializeField]
        private float groundSlamSlowAmount = 0.3f;

        [SerializeField]
        private float groundSlamSlowDuration = 2f;

        [SerializeField]
        private float groundSlamCooldown = 5f;

        [SerializeField]
        private float groundSlamActionDuration = 0.5f;

        // Shared post-attack lock: briefly blocks every ability, regardless
        // of which one was just used, before each ability's own longer cooldown takes over.
        [Header("Attack Shared")]
        [SerializeField]
        private float characterCooldownDuration = 0.5f;

        [SyncVar(hook = nameof(OnOwnerNetIdChanged))]
        private uint ownerNetId;

        [SyncVar(hook = nameof(OnStatusChanged))]
        private NemesisStatus _status;

        // Server time when the 60s lifetime expires. Exposed for the (later) Nemesis HUD lifetime bar.
        [SyncVar]
        private double _lifetimeEndNetworkTime;

        // True for the brief window an attack's effect/action is resolving — all abilities are locked
        // out while true.
        [SyncVar]
        private bool _isAttacking;

        [SyncVar]
        private double _characterCooldownEndNetworkTime;

        [SyncVar]
        private double _punchReadyNetworkTime;

        [SyncVar]
        private double _lungeReadyNetworkTime;

        [SyncVar]
        private double _groundSlamReadyNetworkTime;

        // Fired on every peer after an attack resolves. No animator/VFX exists yet — this is the hook
        // future art/audio work subscribes to; the HUD also uses it to refresh cooldown displays.
        public event Action<NemesisAttackType> OnAttackPerformedEvent;

        private GameplayPlayer _attachedOwner;
        private Rigidbody _rb;

        public uint OwnerNetId => ownerNetId;
        public NemesisStatus Status => _status;
        public bool IsActive => _status == NemesisStatus.Active;
        public bool IsDisassembling => _status == NemesisStatus.Disassembling;
        public Transform NemesisRoot => nemesisRoot != null ? nemesisRoot : transform;
        public double LifetimeEndNetworkTime => _lifetimeEndNetworkTime;

        // Total lifetime span (seconds), for the HUD to compute the drain fraction.
        public float LifetimeSeconds => lifetime;

        // Seconds left on the 60s lifetime while active (0 once disassembling). Drives the HUD countdown.
        public float LifetimeRemainingSeconds =>
            IsActive ? Mathf.Max(0f, (float)(_lifetimeEndNetworkTime - NetworkTime.time)) : 0f;

        public bool IsAttacking => _isAttacking;
        public bool IsCharacterCoolingDown => NetworkTime.time < _characterCooldownEndNetworkTime;

        // Every peer already has these SyncVars replicated (no client-side prediction clock needed,
        // unlike the Turret's high-frequency fire cooldown), so the owner can gate input locally with
        // the same check the server re-validates.
        public bool IsAttackAvailable(NemesisAttackType type)
        {
            if (_status != NemesisStatus.Active || _isAttacking || IsCharacterCoolingDown)
            {
                return false;
            }

            return NetworkTime.time >= GetAttackReadyTime(type);
        }

        // 0 = just used (full cooldown remaining) → 1 = ready. Drives the HUD's per-ability radial fill.
        public float GetAttackCooldownFraction(NemesisAttackType type)
        {
            float duration = GetAttackCooldownDuration(type);
            if (duration <= 0f)
            {
                return 1f;
            }

            double remaining = GetAttackReadyTime(type) - NetworkTime.time;
            return 1f - Mathf.Clamp01((float)(remaining / duration));
        }

        public float GetAttackCooldownDuration(NemesisAttackType type)
        {
            return type switch
            {
                NemesisAttackType.Punch => punchCooldown,
                NemesisAttackType.Lunge => lungeCooldown,
                NemesisAttackType.GroundSlam => groundSlamCooldown,
                _ => 0f,
            };
        }

        private double GetAttackReadyTime(NemesisAttackType type)
        {
            return type switch
            {
                NemesisAttackType.Punch => _punchReadyNetworkTime,
                NemesisAttackType.Lunge => _lungeReadyNetworkTime,
                NemesisAttackType.GroundSlam => _groundSlamReadyNetworkTime,
                _ => 0d,
            };
        }

        [Server]
        public bool ServerTryExecuteAttack(NemesisAttackType type)
        {
            if (!IsAttackAvailable(type))
            {
                return false;
            }

            _isAttacking = true;
            switch (type)
            {
                case NemesisAttackType.Punch:
                    StartCoroutine(ServerPunchRoutine());
                    break;
                case NemesisAttackType.Lunge:
                    StartCoroutine(ServerLungeRoutine());
                    break;
                case NemesisAttackType.GroundSlam:
                    StartCoroutine(ServerGroundSlamRoutine());
                    break;
            }

            return true;
        }

        [Server]
        private IEnumerator ServerPunchRoutine()
        {
            yield return new WaitForSeconds(punchActionDuration);
            ServerApplyPunch();
            ServerFinishAttack(NemesisAttackType.Punch);
        }

        [Server]
        private IEnumerator ServerLungeRoutine()
        {
            yield return new WaitForSeconds(lungeActionDuration);
            ServerApplyLunge();
            ServerFinishAttack(NemesisAttackType.Lunge);
        }

        [Server]
        private IEnumerator ServerGroundSlamRoutine()
        {
            yield return new WaitForSeconds(groundSlamActionDuration);
            ServerApplyGroundSlam();
            ServerFinishAttack(NemesisAttackType.GroundSlam);
        }

        [Server]
        private void ServerFinishAttack(NemesisAttackType type)
        {
            _isAttacking = false;
            _characterCooldownEndNetworkTime = NetworkTime.time + characterCooldownDuration;

            double readyTime = NetworkTime.time + GetAttackCooldownDuration(type);
            switch (type)
            {
                case NemesisAttackType.Punch:
                    _punchReadyNetworkTime = readyTime;
                    break;
                case NemesisAttackType.Lunge:
                    _lungeReadyNetworkTime = readyTime;
                    break;
                case NemesisAttackType.GroundSlam:
                    _groundSlamReadyNetworkTime = readyTime;
                    break;
            }

            RpcAttackPerformed((int)type);
        }

        [ClientRpc]
        private void RpcAttackPerformed(int attackType)
        {
            OnAttackPerformedEvent?.Invoke((NemesisAttackType)attackType);
        }

        // Close-range AOE centered on the Nemesis.
        [Server]
        private void ServerApplyPunch()
        {
            Vector3 center = transform.position;
            var hitPlayers = new HashSet<GameplayPlayer>();

            foreach (
                Collider col in Physics.OverlapSphere(
                    center,
                    punchRadius,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                GameplayPlayer target = col.GetComponentInParent<GameplayPlayer>();
                if (target == null || hitPlayers.Contains(target) || !IsValidSurvivorTarget(target))
                {
                    continue;
                }

                hitPlayers.Add(target);
                target.health.ServerTakeDamage(punchDamage, netId, center);
            }
        }

        // Damage-checks a capsule swept along the Nemesis's current facing. The actual forward motion is
        // left to the owner's client-authoritative movement (OwnerMove) rather than server-repositioning
        // the Nemesis, so this doesn't fight that ownership model.
        [Server]
        private void ServerApplyLunge()
        {
            Vector3 origin = transform.position;
            Vector3 forward = NemesisRoot.forward;
            Vector3 end = origin + forward * lungeDistance;
            var hitPlayers = new HashSet<GameplayPlayer>();

            foreach (
                Collider col in Physics.OverlapCapsule(
                    origin,
                    end,
                    lungeRadius,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                GameplayPlayer target = col.GetComponentInParent<GameplayPlayer>();
                if (target == null || hitPlayers.Contains(target) || !IsValidSurvivorTarget(target))
                {
                    continue;
                }

                hitPlayers.Add(target);
                target.health.ServerTakeDamage(lungeDamage, netId, target.transform.position);
                target.ServerApplyKnockback(forward * lungeKnockbackForce);
            }
        }

        // AOE centered on the Nemesis.
        [Server]
        private void ServerApplyGroundSlam()
        {
            Vector3 center = transform.position;
            var hitPlayers = new HashSet<GameplayPlayer>();

            foreach (
                Collider col in Physics.OverlapSphere(
                    center,
                    groundSlamRadius,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore
                )
            )
            {
                GameplayPlayer target = col.GetComponentInParent<GameplayPlayer>();
                if (target == null || hitPlayers.Contains(target) || !IsValidSurvivorTarget(target))
                {
                    continue;
                }

                hitPlayers.Add(target);
                target.health.ServerTakeDamage(groundSlamDamage, netId, center);
                target.ServerApplySlow(groundSlamSlowAmount, groundSlamSlowDuration);
            }
        }

        // Mirrors C4Trap.IsValidSurvivor — the Nemesis only ever damages living Survivors.
        private static bool IsValidSurvivorTarget(GameplayPlayer player)
        {
            if (player == null || player.IsDungeonMaster || player.IsInactive)
            {
                return false;
            }

            if (player.localManager == null || player.localManager.playerRole != PlayerRole.Survivor)
            {
                return false;
            }

            return player.health != null && player.health.IsAlive;
        }

        private Rigidbody Body => _rb != null ? _rb : _rb = GetComponent<Rigidbody>();

        [Server]
        public void ServerInitialize(GameplayPlayer owner)
        {
            ownerNetId = owner != null ? owner.netId : 0;
            RegisterWithOwner();
            _status = NemesisStatus.Active;
            _lifetimeEndNetworkTime = NetworkTime.time + lifetime;
            StartCoroutine(LifetimeCoroutine());
        }

        [Server]
        private IEnumerator LifetimeCoroutine()
        {
            yield return new WaitForSeconds(lifetime);
            ServerBeginDisassemble();
        }

        [Server]
        public void ServerBeginDisassemble()
        {
            if (_status == NemesisStatus.Disassembling)
            {
                return;
            }

            StopAllCoroutines();
            _status = NemesisStatus.Disassembling;
            StartCoroutine(DisassembleCoroutine());
        }

        [Server]
        private IEnumerator DisassembleCoroutine()
        {
            yield return new WaitForSeconds(disassemblyDuration);
            NetworkServer.Destroy(gameObject);
        }

        // Drives the Nemesis from the possessing client (which owns this entity's NetworkTransform).
        // groundDir is a world-space, ground-plane direction derived from the Dungeon Master's camera.
        public void OwnerMove(Vector3 groundDir)
        {
            if (!isOwned || _status != NemesisStatus.Active)
            {
                return;
            }

            Vector3 flat = Vector3.ProjectOnPlane(groundDir, Vector3.up);
            if (flat.sqrMagnitude > 1f)
            {
                flat = flat.normalized;
            }

            float baseSpeed = _attachedOwner != null ? _attachedOwner.moveSpeed : fallbackBaseSpeed;
            Vector3 delta = baseSpeed * speedMultiplier * Time.fixedDeltaTime * flat;
            if (Body != null)
            {
                Body.MovePosition(Body.position + delta);
            }
            else
            {
                transform.position += delta;
            }

            if (flat.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(flat.normalized, Vector3.up);
                Transform root = NemesisRoot;
                root.rotation = Quaternion.RotateTowards(
                    root.rotation,
                    target,
                    turnSpeed * Time.fixedDeltaTime
                );
            }
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RegisterWithOwner();
        }

        public override void OnStopClient()
        {
            DetachFromOwner();
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            DetachFromOwner();
            base.OnStopServer();
        }

        public bool IsOwnedBy(GameplayPlayer owner)
        {
            return owner != null && ownerNetId == owner.netId;
        }

        private void OnOwnerNetIdChanged(uint oldValue, uint newValue)
        {
            if (oldValue != newValue)
            {
                DetachFromOwner();
            }

            RegisterWithOwner();
        }

        private void OnStatusChanged(NemesisStatus _, NemesisStatus next)
        {
            if (next == NemesisStatus.Active)
            {
                TryEnterControl();
            }
        }

        private void RegisterWithOwner()
        {
            if (_attachedOwner != null || ownerNetId == 0)
            {
                return;
            }

            GameplayPlayer owner = ResolveOwner();
            if (owner == null)
            {
                return;
            }

            _attachedOwner = owner;
            owner.Nemesis.AttachSpawnedNemesis(this);

            // The status SyncVar may have arrived before the owner was resolvable (spawn ordering), so
            // the OnStatusChanged hook can miss its chance to hand over control — catch up here.
            TryEnterControl();
        }

        // Hands camera/movement control to the owning Dungeon Master by queueing the Nemesis state on
        // their state machine. Guarded so repeated calls (status hook + register catch-up) don't
        // re-enter the state every frame.
        private void TryEnterControl()
        {
            if (_attachedOwner == null || !_attachedOwner.isLocalPlayer || _status != NemesisStatus.Active)
            {
                return;
            }

            if (_attachedOwner.currentState is DungeonMasterNemesisState
                || _attachedOwner.nextState is DungeonMasterNemesisState)
            {
                return;
            }

            _attachedOwner.QueueState(new DungeonMasterNemesisState(_attachedOwner));
        }

        private void DetachFromOwner()
        {
            if (_attachedOwner == null)
            {
                return;
            }

            _attachedOwner.Nemesis.DetachSpawnedNemesis(this);
            _attachedOwner = null;
        }

        private GameplayPlayer ResolveOwner()
        {
            if (NetworkClient.active
                && NetworkClient.spawned.TryGetValue(ownerNetId, out NetworkIdentity clientIdentity))
            {
                return clientIdentity.GetComponentInChildren<GameplayPlayer>();
            }

            if (NetworkServer.active
                && NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity serverIdentity))
            {
                return serverIdentity.GetComponentInChildren<GameplayPlayer>();
            }

            return null;
        }
    }
}
