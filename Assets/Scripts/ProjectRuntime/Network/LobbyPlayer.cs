using Mirror;
using ProjectRuntime.UI;
using Steamworks;

namespace ProjectRuntime.Network
{
    public class LobbyPlayer : NetworkBehaviour
    {
        // Player Data 
        [SyncVar] public int ConnectionID;
        [SyncVar] public int PlayerIdNumber;
        [SyncVar] public ulong PlayerSteamID;
        [SyncVar(hook = nameof(PlayerNameUpdate))] public string PlayerName;
        [SyncVar(hook = nameof(PlayerReadyUpdate))] public bool IsReady;

        public override void OnStartLocalPlayer()
        {
            this.CmdSetPlayerName(SteamFriends.GetPersonaName());
            PnlLobby.Instance.LocalPlayerController = this;
        }

        public override void OnStartAuthority()
        {
            this.CmdSetPlayerName(SteamFriends.GetPersonaName().ToString());
            PnlLobby.Instance.UpdateLobbyName();
        }

        public override void OnStartClient()
        {
            GameNetworkManager.Instance.LobbyPlayers.Add(this);
            PnlLobby.Instance.UpdateLobbyName();
            PnlLobby.Instance.UpdatePlayerList();
        }

        public override void OnStopClient()
        {
            GameNetworkManager.Instance.LobbyPlayers.Remove(this);
            if (PnlLobby.Instance != null)
            {
                PnlLobby.Instance.UpdatePlayerList();
            }
        }

        [Command]
        private void CmdSetPlayerName(string playername)
        {
            this.PlayerNameUpdate(this.PlayerName, playername);
        }

        private void PlayerNameUpdate(string oldValue, string newValue)
        {
            if (this.isServer)
            {
                this.PlayerName = newValue;
            }

            if (this.isClient)
            {
                PnlLobby.Instance.UpdatePlayerList();
            }
        }

        public void ChangeReady()
        {
            if (this.isOwned)
            {
                this.CmdSetPlayerReady();
            }
        }

        [Command]
        private void CmdSetPlayerReady()
        {
            PlayerReadyUpdate(this.IsReady, !this.IsReady);
        }

        private void PlayerReadyUpdate(bool _, bool newValue)
        {
            if (isServer)
            {
                this.IsReady = newValue;
            }
            if (isClient)
            {
                PnlLobby.Instance.UpdatePlayerList();
            }
        }

        public void CanStartGame(string sceneName)
        {
            if (this.authority)
            {
                this.CmdCanStartGame(sceneName);
            }
        }
        [Command]
        private void CmdCanStartGame(string sceneName)
        {
            GameNetworkManager.Instance.StartGame(sceneName);
        }
    }
}