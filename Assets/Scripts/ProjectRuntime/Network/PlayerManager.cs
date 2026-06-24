using Mirror;
using ProjectRuntime.Actor;
using ProjectRuntime.Network.Steam;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace ProjectRuntime.Network
{
    /// <summary>
    /// This is a server to client class, server authoritative variables should be stored here instead of on player
    /// </summary>
    public class PlayerManager : NetworkSingleton<PlayerManager>
    {
        [Header("Components")]
        [field: SerializeField] public GameplayPlayer player {get; private set;}
        [field: SerializeField] public Canvas playerCanvas { get; private set; }
        [field: SerializeField] public TextMeshProUGUI playerNameText { get; private set; }

        [Header("Syncvars")]
        [SyncVar(hook = nameof(OnPlayerNameSynced))] public string playerName;
        [SyncVar] public ulong playerSteamId;
        [SyncVar] public int playerIndex;
        public Action<string> OnPlayerNameChanged;

        [Header("Variables")]
        [SyncVar(hook = nameof(OnCharacterDataSynced))] public CharacterData characterData;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (SteamManager.Initialized)
                CmdSendSteamIdentity(SteamUser.GetSteamID().m_SteamID, SteamFriends.GetPersonaName());

            StartupSpawned(this);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Hide own canvas, only show if other player
            playerCanvas.gameObject.SetActive(!isLocalPlayer);
        }

        [TargetRpc]
        public void RpcSyncNetworkSMsToPlayer(NetworkConnectionToClient target, List<NetworkBaseState> states)
        {
            foreach (var state in states)
            {
                NetworkStateMachine sm = state.sm;
                sm.currentState = state;
                sm.currentState.OnEnter();
            }
        }

        [Command]
        void CmdSendSteamIdentity(ulong steamId, string name)
        {
            playerName = name;
            playerSteamId = steamId;

            var auth = GameNetworkManager.Instance.SteamAuth;
            if (auth != null)
                auth.UpdateIdentity(steamId, name);
        }

        void OnPlayerNameSynced(string oldValue, string newValue)
        {
            playerName = newValue;
            if (playerNameText != null)
                playerNameText.text = playerName;

            OnPlayerNameChanged?.Invoke(newValue);
        }

        void OnCharacterDataSynced(CharacterData _, CharacterData newData)
        {
            characterData = newData;
        }
    }

    [Serializable]
    public struct CharacterData
    {
        public int colid;
    }
}
