using System.Collections;
using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public enum NemesisStatus
    {
        Active,
        Disassembling,
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
        // Speed as a multiple of the possessing player's survivor moveSpeed (GDD: 1.1x survivor).
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

        [SyncVar(hook = nameof(OnOwnerNetIdChanged))]
        private uint ownerNetId;

        [SyncVar(hook = nameof(OnStatusChanged))]
        private NemesisStatus _status;

        // Server time when the 60s lifetime expires. Exposed for the (later) Nemesis HUD lifetime bar.
        [SyncVar]
        private double _lifetimeEndNetworkTime;

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
