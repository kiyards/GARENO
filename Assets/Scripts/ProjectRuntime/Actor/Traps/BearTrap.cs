using Mirror;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkIdentity))]
    [RequireComponent(typeof(Health))]
    [RequireComponent(typeof(Rigidbody))]
    public class BearTrap : NetworkBehaviour, ITrap
    {
        [Header("Health")]
        [SerializeField]
        private float maxHealth = 500f;

        [Header("Damage")]
        [SerializeField]
        private float initialDamage = 10f;

        [SerializeField]
        private float tickDamage = 5f;

        [SerializeField]
        private float tickInterval = 1f;

        [Header("Escape")]
        [SerializeField]
        private float requiredMashCount = 20f;

        [SerializeField]
        private float mashIncrement = 1f;

        [SerializeField]
        private float mashDrainRate = 1.5f;

        [SyncVar(hook = nameof(OnTriggeredSynced))]
        private bool isTriggered;

        [SyncVar]
        private uint trappedPlayerNetId;

        [SyncVar]
        private float mashCount;

        private Health _health;
        private GameplayPlayer _trappedPlayer;
        private double _nextDamageTime;
        private Renderer[] _renderers;

        public bool IsTriggered => isTriggered;
        public uint TrappedPlayerNetId => trappedPlayerNetId;
        public float MashProgress => requiredMashCount > 0f ? mashCount / requiredMashCount : 0f;

        private void Awake()
        {
            CacheComponents();
            ConfigureComponents();
            ApplyTriggeredVisual(isTriggered);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            CacheComponents();
            ConfigureComponents();
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            CacheComponents();
            ConfigureComponents();
            isTriggered = false;
            trappedPlayerNetId = 0;
            mashCount = 0;
            _trappedPlayer = null;

            _health.OnDeathEvent += OnHealthDepleted;
        }

        public override void OnStopServer()
        {
            _health.OnDeathEvent -= OnHealthDepleted;

            base.OnStopServer();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            CacheComponents();
            ApplyTriggeredVisual(isTriggered);
        }

        private void FixedUpdate()
        {
            if (!isServer || !isTriggered)
            {
                return;
            }

            if (_trappedPlayer == null || !_trappedPlayer.health.IsAlive)
            {
                ServerDestroyTrap();
                return;
            }

            mashCount = Mathf.Max(0f, mashCount - mashDrainRate * Time.fixedDeltaTime);

            if (NetworkTime.time < _nextDamageTime)
            {
                return;
            }

            _trappedPlayer.health.ServerTakeDamage(tickDamage, netId, transform.position);
            _nextDamageTime = NetworkTime.time + tickInterval;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isServer || isTriggered)
            {
                return;
            }

            var player = other.GetComponentInParent<GameplayPlayer>();
            if (!IsValidSurvivor(player))
            {
                return;
            }

            ServerTrapPlayer(player);
        }

        [Server]
        public void ServerHandleMash(GameplayPlayer player)
        {
            if (!isTriggered || player == null || player != _trappedPlayer)
            {
                return;
            }

            mashCount = Mathf.Min(requiredMashCount, mashCount + mashIncrement);
            if (mashCount >= requiredMashCount)
            {
                ServerReleaseTrappedPlayer();
                ServerDestroyTrap();
            }
        }

        [Server]
        private void ServerTrapPlayer(GameplayPlayer player)
        {
            isTriggered = true;
            _trappedPlayer = player;
            trappedPlayerNetId = player.netId;
            mashCount = 0;
            _nextDamageTime = NetworkTime.time + tickInterval;

            Vector3 anchorPosition = player.rb.position;
            player.ServerEnterBearTrap(this, anchorPosition);
            player.health.ServerTakeDamage(initialDamage, netId, transform.position);
        }

        [Server]
        private void ServerReleaseTrappedPlayer()
        {
            if (_trappedPlayer != null && _trappedPlayer.health.IsAlive)
            {
                _trappedPlayer.ServerExitBearTrap(netId);
            }

            _trappedPlayer = null;
            trappedPlayerNetId = 0;
            isTriggered = false;
            mashCount = 0;
        }

        [Server]
        private void ServerDestroyTrap()
        {
            ServerReleaseTrappedPlayer();
            NetworkServer.Destroy(gameObject);
        }

        [Server]
        private void OnHealthDepleted(uint killerNetId)
        {
            ServerDestroyTrap();
        }

        private bool IsValidSurvivor(GameplayPlayer player)
        {
            if (
                player == null
                || player.IsDungeonMaster
                || player.IsInactive
                || player.IsBearTrapped
            )
            {
                return false;
            }

            if (player.localManager.playerRole != PlayerRole.Survivor)
            {
                return false;
            }

            return player.health.IsAlive;
        }

        private void OnTriggeredSynced(bool oldValue, bool newValue)
        {
            ApplyTriggeredVisual(newValue);
        }

        private void CacheComponents()
        {
            _health ??= GetComponent<Health>();
            if (_renderers == null || _renderers.Length == 0)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
        }

        private void ConfigureComponents()
        {
            if (_health != null)
            {
                _health.ConfigureMaxHealth(maxHealth);
            }

            if (TryGetComponent(out Rigidbody trapRigidbody))
            {
                trapRigidbody.useGravity = false;
                trapRigidbody.isKinematic = true;
            }
        }

        private void ApplyTriggeredVisual(bool triggered)
        {
            CacheComponents();

            Color targetColor = triggered
                ? new Color(0.85f, 0.18f, 0.12f, 1f)
                : new Color(0.22f, 0.22f, 0.2f, 1f);

            foreach (Renderer targetRenderer in _renderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.material.color = targetColor;
            }
        }
    }
}
