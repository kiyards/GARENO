using Mirror;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
using System.Collections;
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

        [Header("Visuals")]
        [SerializeField]
        private Animator trapAnimator;

        [SerializeField]
        private Transform shakeRoot;

        [SerializeField]
        private float mashShakeDuration = 0.12f;

        [SerializeField]
        private float mashShakeAmplitude = 0.08f;

        [SerializeField]
        private float mashShakeFrequency = 38f;

        [SerializeField]
        private float mashShakeRotationDegrees = 4f;

        [SerializeField]
        private string snapStateName = "enemy_trap_attack";

        [SyncVar(hook = nameof(OnTriggeredSynced))]
        private bool isTriggered;

        [SyncVar]
        private uint trappedPlayerNetId;

        [SyncVar]
        private float mashCount;

        private Health _health;
        private GameplayPlayer _trappedPlayer;
        private double _nextDamageTime;
        private Vector3 _shakeOriginLocalPosition;
        private Quaternion _shakeOriginLocalRotation;
        private Coroutine _mashShakeCoroutine;
        private bool _triggerVisualPlayed;

        public bool IsTriggered => isTriggered;
        public uint TrappedPlayerNetId => trappedPlayerNetId;
        public float MashProgress => requiredMashCount > 0f ? mashCount / requiredMashCount : 0f;

        private void Awake()
        {
            CacheComponents();
            ConfigureComponents();
            CacheVisualOrigin();
            ApplyTriggeredVisual(isTriggered, false);
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
            CacheVisualOrigin();
            ApplyTriggeredVisual(isTriggered, false);
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
            RpcPlayMashShake();

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
            ApplyTriggeredVisual(newValue, !oldValue && newValue);
        }

        private void CacheComponents()
        {
            _health ??= GetComponent<Health>();
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

        private void CacheVisualOrigin()
        {
            _shakeOriginLocalPosition = shakeRoot.localPosition;
            _shakeOriginLocalRotation = shakeRoot.localRotation;
        }

        private void ApplyTriggeredVisual(bool triggered, bool playSnap)
        {
            if (triggered)
            {
                if (playSnap)
                {
                    PlaySnapVisual();
                    return;
                }

                _triggerVisualPlayed = true;
                trapAnimator.enabled = true;
                trapAnimator.Play(snapStateName, 0, 1f);
                trapAnimator.Update(0f);
                RestoreShakeRoot();
                return;
            }

            _triggerVisualPlayed = false;
            trapAnimator.enabled = true;
            trapAnimator.Play(snapStateName, 0, 0f);
            trapAnimator.Update(0f);
            trapAnimator.enabled = false;
            RestoreShakeRoot();
        }

        private void PlaySnapVisual()
        {
            if (_triggerVisualPlayed)
            {
                return;
            }

            _triggerVisualPlayed = true;
            trapAnimator.enabled = true;
            trapAnimator.Play(snapStateName, 0, 0f);
            trapAnimator.Update(0f);
            RestoreShakeRoot();
        }

        [ClientRpc]
        private void RpcPlayMashShake()
        {
            PlayMashShake();
        }

        private void PlayMashShake()
        {
            if (_mashShakeCoroutine != null)
            {
                StopCoroutine(_mashShakeCoroutine);
            }

            _mashShakeCoroutine = StartCoroutine(ShakeMashVisual());
        }

        private IEnumerator ShakeMashVisual()
        {
            float elapsed = 0f;

            while (elapsed < mashShakeDuration)
            {
                float normalizedTime = elapsed / mashShakeDuration;
                float falloff = 1f - normalizedTime;
                float wave = Mathf.Sin(elapsed * mashShakeFrequency * Mathf.PI * 2f);
                float offset = wave * mashShakeAmplitude * falloff;
                float rotation = wave * mashShakeRotationDegrees * falloff;

                shakeRoot.localPosition = _shakeOriginLocalPosition + new Vector3(offset, 0f, -offset * 0.35f);
                shakeRoot.localRotation = _shakeOriginLocalRotation * Quaternion.Euler(0f, rotation, rotation * 0.35f);

                elapsed += Time.deltaTime;
                yield return null;
            }

            shakeRoot.localPosition = _shakeOriginLocalPosition;
            shakeRoot.localRotation = _shakeOriginLocalRotation;
            _mashShakeCoroutine = null;
        }

        private void RestoreShakeRoot()
        {
            if (_mashShakeCoroutine != null)
            {
                StopCoroutine(_mashShakeCoroutine);
                _mashShakeCoroutine = null;
            }

            shakeRoot.localPosition = _shakeOriginLocalPosition;
            shakeRoot.localRotation = _shakeOriginLocalRotation;
        }
    }
}
