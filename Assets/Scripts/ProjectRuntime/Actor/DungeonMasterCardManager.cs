using System;
using Mirror;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    public class DungeonMasterCardManager : NetworkBehaviour
    {
        [SerializeField] private float manaRegenRate = 1f;
        [SerializeField] private int maxMana = 10;

        [SyncVar(hook = nameof(OnManaChanged))]
        public float Mana;

        public event Action<float, int> OnManaChangedEvent;
        public int MaxMana => this.maxMana;

        private GameplayPlayer _player;
        private GameplayPlayer Player => this._player ??= this.GetComponent<GameplayPlayer>();

        private void Update()
        {
            if (!this.isServer) return;
            if (!this.Player.IsDungeonMaster) return;
            this.Mana = Mathf.Min(this.Mana + this.manaRegenRate * Time.deltaTime, this.maxMana);
        }

        private void OnManaChanged(float oldVal, float newVal)
            => this.OnManaChangedEvent?.Invoke(newVal, this.maxMana);

        [Server]
        public bool ServerTrySpendMana(int amount)
        {
            if (this.Mana < amount) return false;
            this.Mana -= amount;
            return true;
        }
    }
}
