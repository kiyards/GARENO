using Mirror;
using ProjectRuntime.Actor;
using ProjectRuntime.Network.Steam;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectRuntime.Network
{
    public class GameNetworkManager : NetworkManager
    {
        public static GameNetworkManager Instance => singleton as GameNetworkManager;
        private SteamAuthenticator SteamAuth => authenticator as SteamAuthenticator;

        [field: SerializeField, Header("Prefabs")]
        private LobbyPlayer LobbyPlayerPrefab { get; set; }

        public List<LobbyPlayer> LobbyPlayers { get; } = new List<LobbyPlayer>();
        public Dictionary<NetworkConnectionToClient, GameplayPlayer> CurrentConnectedPlayers = new();
        public Dictionary<NetworkConnectionToClient, GameplayPlayer> LifetimeConnectedPlayers = new();
        public CSteamID HostedLobbyId;

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

            var sceneName = SceneManager.GetActiveScene().name;

            switch (sceneName)
            {
                case "ScLobby":
                    this.SpawnLobbyPlayer(conn);
                    break;
                case "ScGame":
                    this.SpawnGamePlayer(conn);
                    break;
            }
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            if (SceneManager.GetActiveScene().name == "ScGame")
            {
                this.LobbyPlayers.Clear();
                this.CurrentConnectedPlayers.Clear();
                this.SetLobbyJoinable(false);
            }
        }
        #endregion

        public void StartGame(string sceneName)
        {
            this.ServerChangeScene(sceneName);
        }

        private void SpawnGamePlayer(NetworkConnectionToClient conn)
        {
            if (conn.identity != null && conn.identity.TryGetComponent<GameplayPlayer>(out _))
            {
                return;
            }

            var startPos = GetStartPosition();
            var spawnPosition = startPos != null ? startPos.position : Vector3.zero;
            var spawnRotation = startPos != null ? startPos.rotation : Quaternion.identity;
            var player = startPos != null
                ? Instantiate(this.playerPrefab, spawnPosition, spawnRotation)
                : Instantiate(this.playerPrefab, spawnPosition, spawnRotation);

            player.name = $"{this.playerPrefab.name} [connId={conn.connectionId}]";

            if (startPos == null && player.TryGetComponent<GameplayPlayer>(out var spawnedPlayer))
            {
                // Keep the player capsule above the floor when the scene has no explicit start point.
                var fallbackHeight = 1.1f;
                var spawnCollider = spawnedPlayer.GetComponentInChildren<Collider>();
                if (spawnCollider != null)
                {
                    fallbackHeight = Mathf.Max(fallbackHeight, spawnCollider.bounds.extents.y + 0.1f);
                }

                player.transform.position = new Vector3(spawnPosition.x, fallbackHeight, spawnPosition.z);
            }

            NetworkServer.AddPlayerForConnection(conn, player);

            var pm = player.GetComponent<GameplayPlayer>();

            if (SteamAuth != null && SteamAuth.TryGetIdentity(conn, out PlayerIdentityData identity))
            {
                pm.Init(identity.playerName, identity.steamId, identity.playerIndex);
            }
            else
            {
                pm.Init("Unknown", 0, 0);
            }

            if (!this.LifetimeConnectedPlayers.ContainsKey(conn))
                this.LifetimeConnectedPlayers.Add(conn, pm);
            else
                this.LifetimeConnectedPlayers[conn] = pm;

            if (!this.CurrentConnectedPlayers.ContainsKey(conn))
                this.CurrentConnectedPlayers.Add(conn, pm);
            else
                this.CurrentConnectedPlayers[conn] = pm;
        }

        private void SpawnLobbyPlayer(NetworkConnectionToClient conn)
        {
            if (conn.identity != null && conn.identity.TryGetComponent<LobbyPlayer>(out _))
            {
                return;
            }

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

        [Server]
        public void ServerSetPaused(bool paused)
        {
            Time.timeScale = paused ? 0f : 1f;
        }

        public void SetLobbyJoinable(bool joinable)
        {
            if (this.HostedLobbyId.IsValid())
                SteamMatchmaking.SetLobbyJoinable(this.HostedLobbyId, joinable);
        }

        public void QuitLobby()
        {
            SteamMatchmaking.LeaveLobby(this.HostedLobbyId);

            if (NetworkClient.localPlayer == null)
            {
                Debug.Log("local player null");
                StopHost();
                return;
            }

            if (NetworkClient.localPlayer.isServer)
            {
                this.StopHost();
            }
            else
            {
                this.StopClient();
            }
        }

        public void QuitGame()
        {
            if (GameplayPlayer.Instance != null)
            {
                Destroy(GameplayPlayer.Instance.gameObject);
            }

            this.StopClient();
        }
    }
}
