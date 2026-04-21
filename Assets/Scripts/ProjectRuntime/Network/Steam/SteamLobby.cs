using Mirror;
using ProjectRuntime.UI;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;

// Made from: https://www.youtube.com/watch?v=7Eoc8U8TWa8
namespace ProjectRuntime.Network.Steam
{
    public class SteamLobby : MonoBehaviour
    {
        public static SteamLobby Instance { get; private set; }

        public ulong CurrentLobbyId;
        private const string HostAddressKey = "HostAddress";
        private string LobbyFilterKey => Application.productName;

        // Lobby Hosting Callbacks
        protected Callback<LobbyCreated_t> LobbyCreated;
        protected Callback<GameLobbyJoinRequested_t> JoinRequested;
        protected Callback<LobbyEnter_t> LobbyEntered;

        // Lobby Menu Callbacks
        protected Callback<LobbyMatchList_t> ListLobbies;
        protected Callback<LobbyDataUpdate_t> LobbyDataUpdated;

        public List<CSteamID> LobbyIds = new();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(this.gameObject);
            }

            if (!SteamManager.Initialized)
            {
                return;
            }

            this.LobbyCreated = Callback<LobbyCreated_t>.Create(this.OnLobbyCreated);
            this.JoinRequested = Callback<GameLobbyJoinRequested_t>.Create(this.OnJoinRequested);
            this.LobbyEntered = Callback<LobbyEnter_t>.Create(this.OnLobbyEntered);

            this.ListLobbies = Callback<LobbyMatchList_t>.Create(this.OnListLobbies);
            this.LobbyDataUpdated = Callback<LobbyDataUpdate_t>.Create(this.OnLobbyDataUpdated);
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        public void HostLobby()
        {
            SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypePublic, GameNetworkManager.Instance.maxConnections);
        }

        private void OnLobbyCreated(LobbyCreated_t callback)
        {
            if (callback.m_eResult != EResult.k_EResultOK)
            {
                return;
            }

            GameNetworkManager.Instance.StartHost();
            SteamMatchmaking.SetLobbyData(new CSteamID(callback.m_ulSteamIDLobby), HostAddressKey, SteamUser.GetSteamID().ToString());
            SteamMatchmaking.SetLobbyData(new CSteamID(callback.m_ulSteamIDLobby), "name",
                SteamFriends.GetPersonaName().ToString() + "'s Lobby");

            GameNetworkManager.Instance.HostedLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
        }

        private void OnJoinRequested(GameLobbyJoinRequested_t callback)
        {
            SteamMatchmaking.JoinLobby(callback.m_steamIDLobby);
        }

        private void OnLobbyEntered(LobbyEnter_t callback)
        {
            // Everyone
            this.CurrentLobbyId = callback.m_ulSteamIDLobby;

            // Clients
            if (NetworkServer.active)
            {
                return;
            }

            GameNetworkManager.Instance.networkAddress = SteamMatchmaking.GetLobbyData(new CSteamID(callback.m_ulSteamIDLobby), HostAddressKey);
            GameNetworkManager.Instance.StartClient();
        }

        public void JoinLobby(CSteamID lobbyId)
        {
            SteamMatchmaking.JoinLobby(lobbyId);
        }

        private void OnListLobbies(LobbyMatchList_t callback)
        {
            if (PnlBrowseLobbies.Instance.UILobbyRowList.Count > 0)
            {
                PnlBrowseLobbies.Instance.ClearAllLobbies();
            }

            for (int i = 0; i < callback.m_nLobbiesMatching; i++)
            {
                CSteamID lobbyId = SteamMatchmaking.GetLobbyByIndex(i);
                this.LobbyIds.Add(lobbyId);
                SteamMatchmaking.RequestLobbyData(lobbyId);
            }
        }

        private void OnLobbyDataUpdated(LobbyDataUpdate_t callback)
        {
            if (PnlBrowseLobbies.Instance != null)
            {
                PnlBrowseLobbies.Instance.DisplayLobbies(this.LobbyIds, callback);
            }
        }

        public void GetLobbiesList()
        {
            if (this.LobbyIds.Count > 0)
            {
                this.LobbyIds.Clear();
            }
            SteamMatchmaking.AddRequestLobbyListStringFilter("lobbyFilter", this.LobbyFilterKey, ELobbyComparison.k_ELobbyComparisonEqual); // Filter out lobbies not for this game
            SteamMatchmaking.AddRequestLobbyListStringFilter(HostAddressKey, SteamUser.GetSteamID().ToString(), ELobbyComparison.k_ELobbyComparisonNotEqual); // Filter out own lobbies created
            SteamMatchmaking.RequestLobbyList();
        }

        public void ClearLobbyList()
        {
            this.CurrentLobbyId = 0;
            this.LobbyIds.Clear();
        }
    }
}