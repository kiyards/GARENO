using Mirror;
using ProjectRuntime.Actor;
using ProjectRuntime.Managers;
using ProjectRuntime.Network.Steam;
using Steamworks;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectRuntime.Network
{
    public enum PlayerRole
    {
        Unassigned,
        Survivor,
        DungeonMaster,
    }

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
        [SyncVar(hook = nameof(OnPlayerRoleSynced))] public PlayerRole playerRole = PlayerRole.Unassigned;
        public Action<string> OnPlayerNameChanged;

        [Header("Variables")]
        [SyncVar(hook = nameof(OnCharacterDataSynced))] public CharacterData characterData;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (SteamManager.Initialized)
                CmdSendSteamIdentity(SteamUser.GetSteamID().m_SteamID, SteamFriends.GetPersonaName());

            StartupSpawned(this);
            PlayerHudManager.EnsureInstance()?.SetLocalPlayer(this);
            ApplyRole(this.playerRole);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyRole(this.playerRole);
        }

        private void Update()
        {
            if (!isLocalPlayer || playerRole != PlayerRole.DungeonMaster)
            {
                return;
            }

            if (Keyboard.current == null ||
                Mouse.current == null ||
                !Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                return;
            }

            if (player == null || player.CardManager == null)
            {
                return;
            }

            if (!TryGetMouseGroundPosition(out Vector3 groundPosition))
            {
                return;
            }

            // Temporary input: play hand slot 0. Slot selection arrives with the hand HUD (Task 2).
            player.CardManager.CmdPlayCard(0, groundPosition);
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

        [Server]
        public void ServerSetRole(PlayerRole role)
        {
            playerRole = role;
            ApplyRole(role);
        }

        void OnPlayerNameSynced(string oldValue, string newValue)
        {
            playerName = newValue;
            if (playerNameText != null)
                playerNameText.text = playerName;

            OnPlayerNameChanged?.Invoke(newValue);
        }

        void OnPlayerRoleSynced(PlayerRole oldValue, PlayerRole newValue)
        {
            ApplyRole(newValue);
        }

        void OnCharacterDataSynced(CharacterData _, CharacterData newData)
        {
            characterData = newData;
        }

        private void ApplyRole(PlayerRole role)
        {
            playerRole = role;

            if (player != null)
            {
                player.ApplyRole(role);
            }

            RefreshPlayerCanvasVisibility();

            if (isLocalPlayer)
            {
                PlayerHudManager.EnsureInstance()?.SetRole(role);
            }
        }

        private void RefreshPlayerCanvasVisibility()
        {
            if (playerCanvas == null)
            {
                return;
            }

            playerCanvas.gameObject.SetActive(!isLocalPlayer && playerRole != PlayerRole.DungeonMaster);
        }

        private static bool TryGetMouseGroundPosition(out Vector3 position)
        {
            position = Vector3.zero;

            Camera camera = Camera.main;
            if (camera == null)
            {
                return false;
            }

            int groundLayer = LayerMask.NameToLayer("Ground");
            if (groundLayer < 0)
            {
                return false;
            }

            Ray ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, 1 << groundLayer))
            {
                return false;
            }

            position = hit.point;
            return true;
        }
    }

    [Serializable]
    public struct CharacterData
    {
        public int colid;
    }
}
