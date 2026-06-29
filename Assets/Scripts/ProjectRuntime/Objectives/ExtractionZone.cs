using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Objectives
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class ExtractionZone : MonoBehaviour
    {
        [SerializeField] private BoxCollider extractionCollider;
        [SerializeField] private GameObject visualRoot;

        private BattleManager _boundBattleManager;

        private void Awake()
        {
            CacheComponents();
            this.extractionCollider.isTrigger = true;
        }

        private void OnEnable()
        {
            TryBindBattleManager();
            RefreshVisual();
        }

        private void OnDisable()
        {
            UnbindBattleManager();
        }

        private void Update()
        {
            if (this._boundBattleManager == null)
            {
                TryBindBattleManager();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            ServerSetPlayerInside(other, true);
        }

        private void OnTriggerStay(Collider other)
        {
            ServerSetPlayerInside(other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            ServerSetPlayerInside(other, false);
        }

        private void ServerSetPlayerInside(Collider other, bool isInside)
        {
            if (!NetworkServer.active || BattleManager.Instance == null)
            {
                return;
            }

            var player = other.GetComponentInParent<PlayerManager>();
            if (player == null || player.playerRole != PlayerRole.Survivor)
            {
                return;
            }

            BattleManager.Instance.ServerSetPlayerInExtraction(player, isInside);
        }

        private void TryBindBattleManager()
        {
            if (this._boundBattleManager == BattleManager.Instance)
            {
                return;
            }

            UnbindBattleManager();

            if (BattleManager.Instance == null)
            {
                RefreshVisual();
                return;
            }

            this._boundBattleManager = BattleManager.Instance;
            this._boundBattleManager.OnRoundStateChanged += RefreshVisual;
            RefreshVisual();
        }

        private void UnbindBattleManager()
        {
            if (this._boundBattleManager == null)
            {
                return;
            }

            this._boundBattleManager.OnRoundStateChanged -= RefreshVisual;
            this._boundBattleManager = null;
        }

        private void RefreshVisual()
        {
            if (this.visualRoot == null)
            {
                return;
            }

            bool shouldShow = BattleManager.Instance != null &&
                              BattleManager.Instance.CurrentRoundPhase == RoundPhase.CrystalsComplete;
            this.visualRoot.SetActive(shouldShow);
        }

        private void CacheComponents()
        {
            this.extractionCollider ??= GetComponent<BoxCollider>();
        }

        private void OnDrawGizmos()
        {
            CacheComponents();
            if (this.extractionCollider == null)
            {
                return;
            }

            Gizmos.color = Color.green;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(this.extractionCollider.center, this.extractionCollider.size);
            Gizmos.matrix = oldMatrix;
        }
    }
}
