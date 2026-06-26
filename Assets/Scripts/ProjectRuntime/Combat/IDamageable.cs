using UnityEngine;

namespace ProjectRuntime.Combat
{
    /// <summary>
    /// Implemented by anything that can take damage (survivors, crystals, enemies, traps).
    /// Damage is always applied on the server.
    /// </summary>
    public interface IDamageable
    {
        void ServerTakeDamage(float amount, uint sourceNetId, Vector3 hitPoint);
        bool IsAlive { get; }
    }
}
