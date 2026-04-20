using Mirror;
using Steamworks;
using System;
using System.Collections.Generic;

[Serializable]
public struct PlayerIdentityData
{
    public ulong steamId;
    public string playerName;
    public int playerIndex;
}

public struct SteamAuthMessage : NetworkMessage
{
    public ulong steamId;
    public string playerName;
}

namespace ProjectRuntime.Network.Steam
{
    public class SteamAuthenticator : NetworkAuthenticator
    {
        /// <summary>Server-only. Maps each authenticated connection to its Steam ID.</summary>
        readonly Dictionary<NetworkConnectionToClient, ulong> _connToSteamId = new();

        /// <summary>Server-only. Authoritative identity store keyed by Steam ID. Persists across scene changes.</summary>
        readonly Dictionary<ulong, PlayerIdentityData> _playerRegistry = new();

        int _nextPlayerIndex;

        public IReadOnlyDictionary<NetworkConnectionToClient, ulong> ConnToSteamId => _connToSteamId;
        public IReadOnlyDictionary<ulong, PlayerIdentityData> PlayerRegistry => _playerRegistry;

        #region Server

        public override void OnStartServer()
        {
            NetworkServer.RegisterHandler<SteamAuthMessage>(OnSteamAuthMessage, false);
        }

        public override void OnStopServer()
        {
            NetworkServer.UnregisterHandler<SteamAuthMessage>();
        }

        public override void OnServerAuthenticate(NetworkConnectionToClient conn) { }

        void OnSteamAuthMessage(NetworkConnectionToClient conn, SteamAuthMessage msg)
        {
            ulong steamId = msg.steamId;
            string playerName = msg.playerName;

            _connToSteamId[conn] = steamId;

            if (!_playerRegistry.ContainsKey(steamId))
            {
                _playerRegistry[steamId] = new PlayerIdentityData
                {
                    steamId = steamId,
                    playerName = playerName,
                    playerIndex = _nextPlayerIndex++,
                };
            }
            else
            {
                var existing = _playerRegistry[steamId];
                existing.playerName = playerName;
                _playerRegistry[steamId] = existing;
            }

            ServerAccept(conn);
        }

        #endregion

        #region Client

        public override void OnClientAuthenticate()
        {
            SteamAuthMessage msg;

            if (SteamManager.Initialized)
            {
                msg = new SteamAuthMessage
                {
                    steamId = SteamUser.GetSteamID().m_SteamID,
                    playerName = SteamFriends.GetPersonaName(),
                };
            }
            else
            {
                msg = new SteamAuthMessage
                {
                    steamId = 0,
                    playerName = $"Player",
                };
            }

            NetworkClient.Send(msg);
            ClientAccept();
        }

        #endregion

        #region Public API (called by GameManager)

        public bool TryGetIdentity(NetworkConnectionToClient conn, out PlayerIdentityData identity)
        {
            identity = default;
            if (_connToSteamId.TryGetValue(conn, out ulong steamId) &&
                _playerRegistry.TryGetValue(steamId, out identity))
                return true;
            return false;
        }

        public void UpdateIdentity(ulong steamId, string playerName)
        {
            if (_playerRegistry.TryGetValue(steamId, out var data))
            {
                data.playerName = playerName;
                _playerRegistry[steamId] = data;
            }
        }

        public void RemoveConnection(NetworkConnectionToClient conn)
        {
            _connToSteamId.Remove(conn);
        }

        public void ClearAll()
        {
            _connToSteamId.Clear();
            _playerRegistry.Clear();
            _nextPlayerIndex = 0;
        }

        #endregion
    }
}