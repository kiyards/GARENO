using Core;
using Mirror;
using ProjectRuntime.Network;

namespace ProjectRuntime.Managers
{
    public class BattleManager : NetworkSingleton<BattleManager>
    {
        public readonly SyncList<PlayerManager> Players = new();
        public int playersToStart = 2;

        private void Awake()
        {
            Startup(this);
        }

        private void OnDestroy()
        {
            DestroyInstance();
        }

        [Server]
        public void ServerAddPlayer(PlayerManager player)
        {
            Players.Add(player);
        }

        [Server]
        public void ServerRemovePlayer(PlayerManager player)
        {
            Players.Remove(player);
        }
    }
}