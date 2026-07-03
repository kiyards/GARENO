using System.Collections;
using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    /// <summary>
    /// A zombie variant that chases the nearest survivor (inherited from <see cref="ZombieEnemy"/>)
    /// and explodes after a short animation when it reaches a target or gets shot to death.
    /// Per the GDD it is faster and tankier than a basic zombie; those stats are configured
    /// on the prefab.
    /// </summary>
    public class CreeperZombie : ZombieEnemy
    {
        [Header("Creeper")]
        [SerializeField] private float explosionRadius = 4f;
        [SerializeField] private float explosionDamage = 50f;
        [SerializeField] private float explosionAnimationDuration = 1.25f;
        // Layers checked for survivors caught in the blast. Set to the player layer(s) on the prefab.
        [SerializeField] private LayerMask explosionMask = ~0;

        // Guards against attack and death paths firing the explosion more than once.
        private bool _hasExploded;

        public override void OnStartServer()
        {
            base.OnStartServer();
            this._hasExploded = false;
        }

        [Server]
        protected override void ServerTick()
        {
            if (this._hasExploded)
            {
                return;
            }

            if (this.IsTargetable && this.ServerHasSurvivorInExplosionTriggerRange())
            {
                this.ServerStartExplosionSequence();
                return;
            }

            base.ServerTick();
        }

        [Server]
        protected override void ServerBeginAttack(GameplayPlayer target)
        {
            this.ServerStartExplosionSequence();
        }

        [Server]
        protected override void OnServerDeath(uint killerNetId)
        {
            this.ServerPrepareForDeath(killerNetId);
            this.ServerStartExplosionSequence();
        }

        [Server]
        private void ServerStartExplosionSequence()
        {
            if (this._hasExploded)
            {
                return;
            }

            this._hasExploded = true;
            this.StopAgent();
            this.ServerSetVisualState(ZombieVisualState.Explode);
            this.StartCoroutine(this.ServerExplodeAfterAnimation());
        }

        [Server]
        private IEnumerator ServerExplodeAfterAnimation()
        {
            float duration = this.GetVisualStateAnimationDuration(
                ZombieVisualState.Explode,
                this.explosionAnimationDuration);

            yield return new WaitForSeconds(duration);
            this.ServerExplode();
        }

        [Server]
        private bool ServerHasSurvivorInExplosionTriggerRange()
        {
            if (BattleManager.Instance == null)
            {
                return false;
            }

            float maxSqrDistance = this.AttackRange * this.AttackRange;
            foreach (PlayerManager pm in BattleManager.Instance.Players)
            {
                GameplayPlayer player = pm != null ? pm.player : null;
                if (!this.IsValidTarget(player))
                {
                    continue;
                }

                if ((this.transform.position - player.transform.position).sqrMagnitude <= maxSqrDistance)
                {
                    return true;
                }
            }

            return false;
        }

        [Server]
        private void ServerExplode()
        {
            Collider[] hits = Physics.OverlapSphere(
                this.transform.position,
                this.explosionRadius,
                this.explosionMask,
                QueryTriggerInteraction.Ignore);

            foreach (Collider hit in hits)
            {
                GameplayPlayer player = hit.GetComponentInParent<GameplayPlayer>();
                if (player == null ||
                    player.localManager == null ||
                    player.localManager.playerRole != PlayerRole.Survivor ||
                    player.health == null ||
                    !player.health.IsAlive)
                {
                    continue;
                }

                player.health.ServerTakeDamage(
                    this.explosionDamage,
                    this.netId,
                    this.transform.position);
            }

            // Destroy directly so the delayed blast does not re-enter the Health death path.
            NetworkServer.Destroy(this.gameObject);
        }
    }
}
