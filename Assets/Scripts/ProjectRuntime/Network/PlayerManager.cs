using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Actor;
using ProjectRuntime.Managers;
using ProjectRuntime.Network.Steam;
using Steamworks;
using TMPro;
using UnityEngine;

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
        [field: SerializeField]
        public GameplayPlayer player { get; private set; }

        [field: SerializeField]
        public Canvas playerCanvas { get; private set; }

        [field: SerializeField]
        public TextMeshProUGUI playerNameText { get; private set; }

        [Header("Syncvars")]
        [SyncVar(hook = nameof(OnPlayerNameSynced))]
        public string playerName;

        [SyncVar]
        public ulong playerSteamId;

        [SyncVar]
        public int playerIndex;

        [SyncVar(hook = nameof(OnPlayerRoleSynced))]
        public PlayerRole playerRole = PlayerRole.Unassigned;
        public Action<string> OnPlayerNameChanged;
        public Action<PlayerRole> OnPlayerRoleChanged;
        public Action<int> OnPlayerLivesChanged;

        [Header("Lives")]
        [SerializeField] private int maxLives = 3;
        // Server-authoritative remaining lives. A survivor is permanently dead at 0 (resolution lives
        // in GameplayPlayer.ServerResolveDowned).
        [SyncVar(hook = nameof(OnLivesSynced))] public int lives = 3;

        public int MaxLives => maxLives;
        public bool IsPermanentlyDead => lives <= 0;

        [Header("Variables")]
        [SyncVar(hook = nameof(OnCharacterDataSynced))]
        public CharacterData characterData;

        [Header("Dungeon Master")]
        [SerializeField]
        private LayerMask dungeonMasterPlacementMask = Physics.DefaultRaycastLayers;

        public override void OnStartLocalPlayer()
        {
            base.OnStartLocalPlayer();

            if (SteamManager.Initialized)
                CmdSendSteamIdentity(
                    SteamUser.GetSteamID().m_SteamID,
                    SteamFriends.GetPersonaName()
                );

            StartupSpawned(this);
            PlayerHudManager.EnsureInstance()?.SetLocalPlayer(this);
            ApplyRole(this.playerRole);
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ApplyRole(this.playerRole);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            lives = maxLives;
        }

        [Server]
        public void ServerLoseLives(int amount)
        {
            lives = Mathf.Max(0, lives - amount);
        }

        [TargetRpc]
        public void RpcSyncNetworkSMsToPlayer(
            NetworkConnectionToClient target,
            List<NetworkBaseState> states
        )
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

        void OnLivesSynced(int oldValue, int newValue)
        {
            lives = newValue;
            OnPlayerLivesChanged?.Invoke(newValue);
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
                player?.input?.SetCursorLockedForRole(role);
                PlayerHudManager.EnsureInstance()?.SetRole(role);
                GameplayPlayer.RefreshAllGhostVisibility();
            }

            OnPlayerRoleChanged?.Invoke(role);
        }

        private void RefreshPlayerCanvasVisibility()
        {
            if (playerCanvas == null)
            {
                return;
            }

            playerCanvas.gameObject.SetActive(
                !isLocalPlayer && playerRole != PlayerRole.DungeonMaster
            );
        }

        // For a ghost (permanently dead survivor) the name plate follows ghost visibility: shown only to
        // viewers who can see the ghost (the Dungeon Master and other dead players), hidden from living
        // survivors. Honours the same base rule as RefreshPlayerCanvasVisibility (never the local owner
        // or the DM).
        public void RefreshGhostNameVisibility(bool viewerCanSeeGhost)
        {
            if (playerCanvas == null)
            {
                return;
            }

            playerCanvas.gameObject.SetActive(
                viewerCanSeeGhost && !isLocalPlayer && playerRole != PlayerRole.DungeonMaster
            );
        }

        private bool TryGetMouseGroundPosition(out Vector3 position)
        {
            return CursorPlacementUtility.TryGetPlacementFromCursor(
                1000f,
                dungeonMasterPlacementMask,
                out position,
                out _,
                QueryTriggerInteraction.Ignore,
                fallbackToWorldGroundPlane: false
            );
        }
    }

    [Serializable]
    public struct CharacterData
    {
        public int colid;
    }
}
