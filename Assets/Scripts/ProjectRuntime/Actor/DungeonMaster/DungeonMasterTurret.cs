using System.Collections;
using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
using ProjectRuntime.Managers;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public enum TurretStatus
    {
        Assembling,
        Assembled,
        Disassembling,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))]
    public class DungeonMasterTurret : NetworkBehaviour
    {
        [Header("Visuals")]
        [SerializeField]
        private Transform turretRoot;

        [SerializeField]
        private Transform turretYawPivot;

        [SerializeField]
        private Transform turretPitchPivot;

        [SerializeField]
        private Transform turretMuzzle;

        [Header("Combat")]
        [SerializeField]
        private float damage = 20f;

        [SerializeField]
        private float maxRange = 80f;

        [SerializeField]
        private float fireCooldown = 0.18f;

        [SerializeField]
        private int maxAmmo = 50;

        [Header("Assembly")]
        [SerializeField]
        private float assemblyDuration = 2f;

        [SerializeField]
        private float disassemblyDuration = 1.5f;

        [SyncVar(hook = nameof(OnOwnerNetIdChanged))]
        private uint ownerNetId;

        [SyncVar(hook = nameof(OnStatusChanged))]
        private TurretStatus _status;

        [SyncVar(hook = nameof(OnAmmoSynced))]
        private int _currentAmmo;

        private GameplayPlayer _attachedOwner;
        private Health _health;

        public Health Health => _health != null ? _health : _health = GetComponent<Health>();
        public uint OwnerNetId => ownerNetId;
        public bool IsAssembled => _status == TurretStatus.Assembled;
        public bool IsDisassembling => _status == TurretStatus.Disassembling;
        public float Damage => damage;
        public float MaxRange => maxRange;
        public float FireCooldown => fireCooldown;
        public int CurrentAmmo => _currentAmmo;
        public int MaxAmmo => maxAmmo;

        [Server]
        public void ServerInitialize(GameplayPlayer owner)
        {
            ownerNetId = owner != null ? owner.netId : 0;
            _currentAmmo = maxAmmo;
            RegisterWithOwner();
            Health.OnDeathEvent += OnServerDeath;
            _status = TurretStatus.Assembling;
            Debug.Log("[Turret] Assembling...");
            StartCoroutine(AssembleCoroutine());
        }

        [Server]
        public void ServerConsumeAmmo()
        {
            if (_currentAmmo <= 0)
                return;

            _currentAmmo--;
            if (_currentAmmo <= 0)
                ServerBeginDisassemble();
        }

        [Server]
        private IEnumerator AssembleCoroutine()
        {
            yield return new WaitForSeconds(assemblyDuration);
            _status = TurretStatus.Assembled;
            Debug.Log("[Turret] Assembled.");
        }

        [Server]
        public void ServerBeginDisassemble()
        {
            if (_status == TurretStatus.Disassembling)
            {
                return;
            }

            StopAllCoroutines();
            _status = TurretStatus.Disassembling;
            Debug.Log("[Turret] Disassembling...");
            StartCoroutine(DisassembleCoroutine());
        }

        [Server]
        private IEnumerator DisassembleCoroutine()
        {
            yield return new WaitForSeconds(disassemblyDuration);
            Debug.Log("[Turret] Disassembled.");
            NetworkServer.Destroy(gameObject);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            SetVisible(true);
            RegisterWithOwner();
        }

        public override void OnStopClient()
        {
            DetachFromOwner();
            base.OnStopClient();
        }

        public override void OnStopServer()
        {
            Health.OnDeathEvent -= OnServerDeath;
            DetachFromOwner();
            base.OnStopServer();
        }

        [Server]
        private void OnServerDeath(uint killerNetId)
        {
            StopAllCoroutines();
            NetworkServer.Destroy(gameObject);
        }

        private void Update()
        {
            if (_attachedOwner == null)
            {
                RegisterWithOwner();
            }
        }

        public bool IsOwnedBy(GameplayPlayer owner)
        {
            return owner != null && ownerNetId == owner.netId;
        }

        public void SetVisible(bool isVisible)
        {
            Transform root = turretRoot != null ? turretRoot : transform;

            if (turretRoot != null)
            {
                turretRoot.gameObject.SetActive(isVisible);
            }

            foreach (Renderer turretRenderer in root.GetComponentsInChildren<Renderer>(true))
            {
                turretRenderer.enabled = isVisible;
            }
        }

        public void UpdateAim(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 flatDirection = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
            if (turretYawPivot != null && flatDirection.sqrMagnitude > 0.0001f)
            {
                turretYawPivot.rotation = Quaternion.LookRotation(
                    flatDirection.normalized,
                    Vector3.up
                );
            }

            if (turretPitchPivot != null)
            {
                turretPitchPivot.rotation = Quaternion.LookRotation(
                    worldDirection.normalized,
                    Vector3.up
                );
            }
        }

        public Vector3 GetMuzzlePosition()
        {
            return turretMuzzle != null
                ? turretMuzzle.position
                : transform.position + transform.forward * 0.75f;
        }

        private void OnAmmoSynced(int _, int next)
        {
            if (_attachedOwner == null || !_attachedOwner.isLocalPlayer)
                return;

            if (PlayerHudManager.Instance != null)
                PlayerHudManager.Instance.SetTurretAmmo(next, maxAmmo);
        }

        private void OnOwnerNetIdChanged(uint oldValue, uint newValue)
        {
            if (oldValue != newValue)
            {
                DetachFromOwner();
            }

            RegisterWithOwner();
        }

        private void OnStatusChanged(TurretStatus _, TurretStatus next)
        {
            if (_attachedOwner == null || !_attachedOwner.isLocalPlayer)
            {
                return;
            }

            if (next == TurretStatus.Assembling)
            {
                if (PlayerHudManager.Instance != null)
                    PlayerHudManager.Instance.SetTurretAssemblingActive(true);
            }
            else if (next == TurretStatus.Assembled)
            {
                if (PlayerHudManager.Instance != null)
                {
                    PlayerHudManager.Instance.SetTurretAmmo(_currentAmmo, maxAmmo);
                    PlayerHudManager.Instance.SetTurretAmmoActive(true);
                }
                _attachedOwner.QueueState(
                    new DungeonMasterTurretState(_attachedOwner)
                    {
                        m_anchorPosition = transform.position,
                        m_hasAnchor = true,
                    }
                );
            }
            else if (next == TurretStatus.Disassembling)
            {
                if (PlayerHudManager.Instance != null)
                {
                    PlayerHudManager.Instance.SetTurretReticleActive(false);
                    PlayerHudManager.Instance.SetTurretAmmoActive(false);
                }
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
            owner.Turret.AttachSpawnedTurret(this);

            if (owner.isLocalPlayer && _status == TurretStatus.Assembling && PlayerHudManager.Instance != null)
                PlayerHudManager.Instance.SetTurretAssemblingActive(true);
        }

        private void DetachFromOwner()
        {
            if (_attachedOwner == null)
            {
                return;
            }

            _attachedOwner.Turret.DetachSpawnedTurret(this);
            _attachedOwner = null;
        }

        private GameplayPlayer ResolveOwner()
        {
            if (
                NetworkClient.active
                && NetworkClient.spawned.TryGetValue(ownerNetId, out NetworkIdentity clientIdentity)
            )
            {
                return clientIdentity.GetComponentInChildren<GameplayPlayer>();
            }

            if (
                NetworkServer.active
                && NetworkServer.spawned.TryGetValue(ownerNetId, out NetworkIdentity serverIdentity)
            )
            {
                return serverIdentity.GetComponentInChildren<GameplayPlayer>();
            }

            return null;
        }
    }
}
