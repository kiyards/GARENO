using Mirror;
using ProjectRuntime.Network;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    /// <summary>
    /// A zombie variant that chases the nearest survivor (inherited from <see cref="ZombieEnemy"/>)
    /// and, instead of meleeing, explodes once it gets within range — dealing AOE damage to all
    /// survivors nearby and then destroying itself. Per the GDD it is faster and tankier than a
    /// basic zombie; those stats (health, move speed, range) are configured on the prefab.
    /// </summary>
    public class CreeperZombie : ZombieEnemy
    {
        [Header("Creeper")]
        [SerializeField] private float explosionRadius = 4f;
        [SerializeField] private float explosionDamage = 50f;
        // Layers checked for survivors caught in the blast. Set to the player layer(s) on the prefab.
        [SerializeField] private LayerMask explosionMask = ~0;

        // Guards against the cooldown-driven attack path firing the explosion more than once.
        private bool _hasExploded;

        [Server]
        protected override void ServerAttack(GameplayPlayer target)
        {
            if (this._hasExploded)
            {
                return;
            }

            this._hasExploded = true;
            this.ServerExplode();
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

            // Self-detonation is not a survivor kill, so we destroy directly rather than routing
            // through Health death — that keeps the zombie-kill timer bonus reserved for survivors
            // who actually shoot a creeper down before it reaches them.
            NetworkServer.Destroy(this.gameObject);
        }
    }
}
