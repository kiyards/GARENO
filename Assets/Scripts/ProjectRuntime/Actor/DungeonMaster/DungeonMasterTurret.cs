using System.Collections;
using Mirror;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Combat;
using ProjectRuntime.Managers;
using ProjectRuntime.UI;
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

        [SerializeField]
        private float placementHeightOffset = 1f;

        [Header("Combat")]
        [SerializeField]
        private float damage = 20f;

        [SerializeField]
        private float maxRange = 80f;

        [SerializeField]
        private float fireCooldown = 0.18f;

        [SerializeField]
        private int maxAmmo = 50;

        [Header("Slow")]
        [SerializeField]
        private bool slowOnHit = false;

        [SerializeField]
        private float slowAmount = 0.2f;

        [SerializeField]
        private float slowDuration = 3f;

        [Header("FX")]
        [SerializeField]
        private DamagePopup damagePopupPrefab;

        [Header("Lifetime")]
        [SerializeField]
        private float lifetime = 20f;

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

        [SyncVar]
        private double _lifetimeEndNetworkTime;

        [SyncVar]
        private double _disassemblyEndNetworkTime;

        [SyncVar(hook = nameof(OnAimDirectionChanged))]
        private Vector3 _syncedAimDirection;

        private GameplayPlayer _attachedOwner;
        private Health _health;
        private bool _visibilityRequested = true;

        public Health Health => _health != null ? _health : _health = GetComponent<Health>();
        public uint OwnerNetId => ownerNetId;
        public bool IsAssembled => _status == TurretStatus.Assembled;
        public bool IsDisassembling => _status == TurretStatus.Disassembling;
        public float Damage => damage;
        public float MaxRange => maxRange;
        public float FireCooldown => fireCooldown;
        public int CurrentAmmo => _currentAmmo;
        public int MaxAmmo => maxAmmo;
        public bool SlowOnHit => slowOnHit;
        public float SlowAmount => slowAmount;
        public float SlowDuration => slowDuration;
        public float PlacementHeightOffset => placementHeightOffset;

        [Server]
        public void ServerInitialize(GameplayPlayer owner)
        {
            ownerNetId = owner != null ? owner.netId : 0;
            _currentAmmo = maxAmmo;
            RegisterWithOwner();
            Health.OnDeathEvent += OnServerDeath;
            _status = TurretStatus.Assembled;
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
        public void ServerStartLifetime()
        {
            if (_status != TurretStatus.Assembled)
                return;

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
            if (_status == TurretStatus.Disassembling)
            {
                return;
            }

            StopAllCoroutines();
            _disassemblyEndNetworkTime = NetworkTime.time + disassemblyDuration;
            _status = TurretStatus.Disassembling;
            StartCoroutine(DisassembleCoroutine());
        }

        [Server]
        public void ServerUpdateAim(GameplayPlayer owner, Vector3 worldDirection)
        {
            if (
                owner == null
                || !IsOwnedBy(owner)
                || _status != TurretStatus.Assembled
                || worldDirection.sqrMagnitude <= 0.0001f
            )
            {
                return;
            }

            _syncedAimDirection = worldDirection.normalized;
            UpdateAim(_syncedAimDirection);
        }

        [Server]
        private IEnumerator DisassembleCoroutine()
        {
            yield return new WaitForSeconds(disassemblyDuration);
            NetworkServer.Destroy(gameObject);
        }

        [ClientRpc]
        public void RpcShowDamageNumber(Vector3 worldPos, float amount)
        {
            DamagePopup.Spawn(damagePopupPrefab, worldPos, amount);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            RegisterWithOwner();
            ApplyVisibility();
            UpdateAim(_syncedAimDirection);
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
                RegisterWithOwner();

            ApplyVisibility();

            if (
                _attachedOwner == null
                || !_attachedOwner.isLocalPlayer
                || PlayerHudManager.Instance == null
            )
                return;

            if (_status == TurretStatus.Assembled)
            {
                float remaining = Mathf.Max(
                    0f,
                    (float)(_lifetimeEndNetworkTime - NetworkTime.time)
                );
                PlayerHudManager.Instance.SetTurretLifetime(remaining, lifetime);
            }
            else if (_status == TurretStatus.Disassembling)
            {
                float fill = Mathf.Clamp01(
                    1f
                        - (float)(_disassemblyEndNetworkTime - NetworkTime.time)
                            / disassemblyDuration
                );
                PlayerHudManager.Instance.SetTurretDisassembling(fill);
            }
        }

        public bool IsOwnedBy(GameplayPlayer owner)
        {
            return owner != null && ownerNetId == owner.netId;
        }

        public void SetVisible(bool isVisible)
        {
            _visibilityRequested = isVisible;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool shouldRender = _visibilityRequested && !ShouldHideForLocalController();
            Transform root = turretRoot != null ? turretRoot : transform;
            if (turretRoot != null)
            {
                turretRoot.gameObject.SetActive(shouldRender);
            }

            foreach (Renderer turretRenderer in root.GetComponentsInChildren<Renderer>(true))
            {
                turretRenderer.enabled = shouldRender;
            }
        }

        private bool ShouldHideForLocalController()
        {
            return _attachedOwner != null
                && _attachedOwner.isLocalPlayer
                && (
                    _attachedOwner.currentState is DungeonMasterTurretState
                    || _attachedOwner.nextState is DungeonMasterTurretState
                );
        }

        public void UpdateAim(Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 aimDirection = worldDirection.normalized;
            Vector3 flatDirection = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
            if (turretYawPivot != null && flatDirection.sqrMagnitude > 0.0001f)
            {
                turretYawPivot.rotation = Quaternion.LookRotation(
                    flatDirection.normalized,
                    Vector3.up
                );
            }

            if (turretPitchPivot != null)
            {
                float horizontalMagnitude = new Vector2(aimDirection.x, aimDirection.z).magnitude;
                float pitchDegrees = Mathf.Atan2(-aimDirection.y, horizontalMagnitude) * Mathf.Rad2Deg;
                turretPitchPivot.localRotation = Quaternion.Euler(pitchDegrees, 0f, 0f);
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

        private void OnAimDirectionChanged(Vector3 _, Vector3 next)
        {
            UpdateAim(next);
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

            if (next == TurretStatus.Assembled)
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
                ApplyVisibility();
            }
            else if (next == TurretStatus.Disassembling)
            {
                if (PlayerHudManager.Instance != null)
                {
                    PlayerHudManager.Instance.SetTurretReticleActive(false);
                    PlayerHudManager.Instance.SetTurretAmmoActive(false);
                    PlayerHudManager.Instance.SetTurretLifetimeActive(false);
                    PlayerHudManager.Instance.SetTurretDisassemblingActive(true);
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
