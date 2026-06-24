using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network.Steam;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectRuntime.Network
{
    [DefaultExecutionOrder(-100)]
    public class GameNetworkManager : NetworkManager
    {
        public static GameNetworkManager Instance => singleton as GameNetworkManager;
        public SteamAuthenticator SteamAuth => authenticator as SteamAuthenticator;

        [Header("Prefabs")]
        [field: SerializeField] private LobbyPlayer LobbyPlayerPrefab { get; set; }

        public List<LobbyPlayer> LobbyPlayers { get; } = new List<LobbyPlayer>();
        public Dictionary<NetworkConnectionToClient, PlayerManager> ConnectedPlayersCurrent = new();
        public Dictionary<(uint, int), NetworkStateMachine> NetId2SM { get; private set; } = new();
        public Dictionary<int, Type> Guid2StateCache = new Dictionary<int, Type>();
        public Dictionary<Type, int> State2GuidCache = new Dictionary<Type, int>();

        public CSteamID HostedLobbyId;

        public override void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }
            base.Awake();
            autoCreatePlayer = false;
            LoadSmAssemblies();
        }

        #region Networked Statemachine Helpers
        void LoadSmAssemblies()
        {
            var listOfTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(domainAssembly => domainAssembly.GetTypes());
            var listOfStates = listOfTypes
                .Where(type => typeof(NetworkBaseState).IsAssignableFrom(type));

            foreach (var state in listOfStates)
            {
                var guid = state.FullName.GetHashCode();
                Guid2StateCache.Add(guid, state);
                State2GuidCache.Add(state, guid);
            }
        }

        public static int SMTypeHash(System.Type smType) => smType.FullName.GetHashCode();

        public void RegisterSM(uint smId, NetworkStateMachine sm)
        {
            NetId2SM[(smId, SMTypeHash(sm.GetType()))] = sm;
        }

        public T GetSM<T>(uint smId) where T : NetworkStateMachine
        {
            return NetId2SM.TryGetValue((smId, SMTypeHash(typeof(T))), out var sm) ? sm as T : null;
        }

        [Server]
        public void ServerSyncNetworkSMsToPlayer(NetworkConnectionToClient target)
        {
            List<NetworkBaseState> states = new();
            foreach (var pair in NetId2SM)
            {
                if (pair.Value == null) continue; // Skip if SM was destroyed but not removed from dict yet (eg from disconnect)
                if (pair.Value.connectionToClient == target) continue; // Skip syncing own states
                if (pair.Value.currentState == null) continue; // Skip if no current state to sync
                states.Add(pair.Value.currentState);
            }
            ConnectedPlayersCurrent[target].RpcSyncNetworkSMsToPlayer(target, states);
        }

        #endregion

        #region Mirror Callbacks
        public override void OnStartServer()
        {
            base.OnStartServer();
            ConnectedPlayersCurrent.Clear();
            NetId2SM.Clear();
            SteamAuth?.ClearAll();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ConnectedPlayersCurrent.Clear();
            NetId2SM.Clear();
            SteamAuth?.ClearAll();
        }

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
                    ServerSyncNetworkSMsToPlayer(conn);
                    break;
            }
        }

        // Player spawning is scene-driven from OnServerReady. Leaving Mirror's
        // auto player path active causes duplicate AddPlayer requests.
        public override void OnServerAddPlayer(NetworkConnectionToClient conn)
        {
        }

        public override void OnServerSceneChanged(string sceneName)
        {
            base.OnServerSceneChanged(sceneName);
            if (SceneManager.GetActiveScene().name == "ScGame")
            {
                this.LobbyPlayers.Clear();
                this.ConnectedPlayersCurrent.Clear();
                this.SetLobbyJoinable(false);
            }
        }

        public override void OnServerDisconnect(NetworkConnectionToClient conn)
        {
            if (conn.identity != null && conn.identity.TryGetComponent(out PlayerManager pm))
                BattleManager.Instance.ServerRemovePlayer(pm);

            ConnectedPlayersCurrent.Remove(conn);
            SteamAuth?.RemoveConnection(conn);

            base.OnServerDisconnect(conn);
        }

        public override Transform GetStartPosition()
        {
            return base.GetStartPosition() == null ? transform : base.GetStartPosition();
        }
        #endregion

        public void StartGame(string sceneName)
        {
            this.ServerChangeScene(sceneName);
        }

        void SpawnGamePlayer(NetworkConnectionToClient conn)
        {
            Transform startPos = GetStartPosition();
            GameObject player = startPos != null
                ? Instantiate(playerPrefab, startPos.position, startPos.rotation)
                : Instantiate(playerPrefab);

            player.name = $"{playerPrefab.name} [connId={conn.connectionId}]";

            var pm = player.GetComponent<PlayerManager>();

            if (SteamAuth != null && SteamAuth.TryGetIdentity(conn, out PlayerIdentityData identity))
            {
                pm.playerName = identity.playerName;
                pm.playerSteamId = identity.steamId;
                pm.playerIndex = identity.playerIndex;
            }
            else
            {
                pm.playerIndex = ConnectedPlayersCurrent.Count;
                pm.playerName = $"Player {pm.playerIndex}";
                pm.playerSteamId = 0;
            }

            NetworkServer.AddPlayerForConnection(conn, player); // triggers OnStartLocalPlayer

            if (!ConnectedPlayersCurrent.ContainsKey(conn))
                ConnectedPlayersCurrent.Add(conn, pm);
            else
                ConnectedPlayersCurrent[conn] = pm;

            if (BattleManager.Instance != null)
                BattleManager.Instance.ServerAddPlayer(pm);

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
            if (PlayerManager.Instance != null)
            {
                PlayerManager.Instance.DestroyInstance();
            }

            this.StopClient();
        }
    }
}
