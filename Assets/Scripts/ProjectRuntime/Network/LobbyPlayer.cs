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
            this.TryRefreshLobbyUi(assignLocalController: true);
        }

        public override void OnStartAuthority()
        {
            this.TryRefreshLobbyUi(assignLocalController: true);
        }

        public override void OnStartClient()
        {
            GameNetworkManager.Instance.LobbyPlayers.Add(this);
            this.TryRefreshLobbyUi();
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
            this.PlayerName = playername;
        }

        private void PlayerNameUpdate(string oldValue, string newValue)
        {
            if (this.isClient)
            {
                this.TryRefreshLobbyUi();
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
            this.IsReady = !this.IsReady;
        }

        private void PlayerReadyUpdate(bool _, bool newValue)
        {
            if (this.isClient)
            {
                this.TryRefreshLobbyUi();
            }
        }

        private void TryRefreshLobbyUi(bool assignLocalController = false)
        {
            if (PnlLobby.Instance == null || GameNetworkManager.Instance == null)
            {
                return;
            }

            if ((assignLocalController || PnlLobby.Instance.LocalPlayerController == null) && this.isOwned)
            {
                PnlLobby.Instance.LocalPlayerController = this;
            }

            if (!GameNetworkManager.Instance.LobbyPlayers.Contains(this))
            {
                return;
            }

            PnlLobby.Instance.UpdateLobbyName();
            PnlLobby.Instance.UpdatePlayerList();
        }

        public void CanStartGame(string sceneName)
        {
            if (this.isOwned)
            {
                this.CmdCanStartGame(sceneName);
            }
        }

        [Command]
        private void CmdCanStartGame(string sceneName)
        {
            if (this.PlayerIdNumber != 0)
            {
                return;
            }

            GameNetworkManager.Instance.StartGame(sceneName);
        }
    }
}
