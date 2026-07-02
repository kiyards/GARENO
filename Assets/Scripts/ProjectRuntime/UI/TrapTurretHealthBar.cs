using System.Collections;
using Mirror;
using ProjectRuntime.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// World-space health bar for traps and turrets.
    /// Shown only to the player who lands a hit, hidden to everyone else.
    /// Add this component alongside a Health component, then drag in the UI references.
    /// </summary>
    public class TrapTurretHealthBar : NetworkBehaviour
    {
        [Header("References")]
        [Tooltip("The root GameObject of the world-space health bar UI. Will be shown/hidden.")]
        [SerializeField] private GameObject _healthBarRoot;

        [Tooltip("Image with Fill Method set to Horizontal. Represents current health.")]
        [SerializeField] private Image _fillImage;

        [Tooltip("Leave empty to auto-find Health on this GameObject.")]
        [SerializeField] private Health _health;

        [Header("Settings")]
        [SerializeField] private float _hideDelay = 3f;

        private Coroutine _hideCoroutine;

        private void Awake()
        {
            if (_health == null)
                _health = GetComponent<Health>();
        }

        public override void OnStartServer()
        {
            if (_health != null)
                _health.OnDamagedEvent += OnServerDamaged;
        }

        public override void OnStopServer()
        {
            if (_health != null)
                _health.OnDamagedEvent -= OnServerDamaged;
        }

        public override void OnStartClient()
        {
            if (_health != null)
                _health.OnHealthChangedEvent += OnClientHealthChanged;

            if (_healthBarRoot != null)
                _healthBarRoot.SetActive(false);
        }

        public override void OnStopClient()
        {
            if (_health != null)
                _health.OnHealthChangedEvent -= OnClientHealthChanged;
        }

        [Server]
        private void OnServerDamaged(float amount, uint sourceNetId, Vector3 hitPoint)
        {
            if (!NetworkServer.spawned.TryGetValue(sourceNetId, out var shooterIdentity))
                return;

            var conn = shooterIdentity.connectionToClient;
            if (conn != null)
                TargetShowHealthBar(conn);
        }

        [TargetRpc]
        private void TargetShowHealthBar(NetworkConnectionToClient target)
        {
            if (_healthBarRoot == null)
                return;

            _healthBarRoot.SetActive(true);
            UpdateFill();

            if (_hideCoroutine != null)
                StopCoroutine(_hideCoroutine);
            _hideCoroutine = StartCoroutine(HideAfterDelay());
        }

        private void OnClientHealthChanged(float current, float max)
        {
            if (_healthBarRoot == null || !_healthBarRoot.activeSelf)
                return;

            UpdateFill(current, max);
        }

        private void UpdateFill()
        {
            if (_health == null || _fillImage == null)
                return;

            _fillImage.fillAmount = _health.MaxHealth > 0f
                ? _health.CurrentHealth / _health.MaxHealth
                : 0f;
        }

        private void UpdateFill(float current, float max)
        {
            if (_fillImage == null)
                return;

            _fillImage.fillAmount = max > 0f ? current / max : 0f;
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSeconds(_hideDelay);
            if (_healthBarRoot != null)
                _healthBarRoot.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_healthBarRoot == null || !_healthBarRoot.activeSelf || Camera.main == null)
                return;

            _healthBarRoot.transform.rotation = Camera.main.transform.rotation;
        }
    }
}
