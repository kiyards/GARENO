using Mirror;
using ProjectRuntime.Network.Steam;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectRuntime.Network
{
    public class GameManager : NetworkManager
    {
        public static GameManager Instance => singleton as GameManager;

        [field: SerializeField, Header("Prefabs")]
        private LobbyPlayer LobbyPlayerPrefab { get; set; }

        public List<LobbyPlayer> LobbyPlayers { get; } = new List<LobbyPlayer>();

        private SteamAuthenticator SteamAuth => authenticator as SteamAuthenticator;

        public override void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            base.Awake();
        }

        #region Mirror Callbacks
        public override void OnServerReady(NetworkConnectionToClient conn)
        {
            base.OnServerReady(conn); // marks conn.isReady = true internally

        }

        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
            if (SceneManager.GetActiveScene().name == "ScLobby")
            {
                var lobbyplayer = Instantiate(this.LobbyPlayerPrefab);
                lobbyplayer.ConnectionID = conn.connectionId;
                lobbyplayer.PlayerIdNumber = this.LobbyPlayers.Count;

                if (this.SteamAuth != null && this.SteamAuth.ConnToSteamId.TryGetValue(conn, out ulong steamId))
                {
                    lobbyplayer.PlayerSteamID = steamId;
                }
                else
                {
                    lobbyplayer.PlayerSteamID = (ulong)SteamMatchmaking.GetLobbyMemberByIndex(
                        (CSteamID)SteamLobby.Instance.CurrentLobbyId,
                        this.LobbyPlayers.Count);
                }

                NetworkServer.AddPlayerForConnection(conn, lobbyplayer.gameObject);
            }
        }
        #endregion

        public void StartGame(string sceneName)
        {
            this.ServerChangeScene(sceneName);
        }
    }
}