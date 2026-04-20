using ProjectRuntime.Network;
using ProjectRuntime.Network.Steam;
using Steamworks;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class PnlLobby : MonoBehaviour
    {
        public static PnlLobby Instance { get; private set; }

        [field: SerializeField, Header("Scene References")]
        private TextMeshProUGUI LobbyNameTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI LobbyCountTMP { get; set; }

        [field: SerializeField]
        private RectTransform PlayerLobbiesRT { get; set; }

        [field: SerializeField]
        private Button LeaveButton { get; set; }

        [field: SerializeField]
        private Button ReadyButton { get; set; }

        [field: SerializeField]
        private Button StartButton { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI ReadyTMP { get; set; }

        [field: SerializeField, Header("Prefabs")]
        private UIPlayerRow UIPlayerLobbyPrefab { get; set; }

        public ulong CurrentLobbyId;
        public bool PlayerItemCreated = false;
        private readonly List<UIPlayerRow> UIPlayerRows = new();
        [HideInInspector] public LobbyPlayer LocalPlayerController;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            this.LeaveButton.onClick.AddListener(this.OnLeaveButtonClick);
            this.ReadyButton.onClick.AddListener(this.OnReadyButtonClick);
            this.StartButton.onClick.AddListener(this.OnStartButtonClick);
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        public void UpdateLobbyName()
        {
            if (SteamLobby.Instance)
            {
                this.CurrentLobbyId = SteamLobby.Instance.CurrentLobbyId;
            }
            this.LobbyNameTMP.text = SteamMatchmaking.GetLobbyData(new CSteamID(this.CurrentLobbyId), "name");
        }

        public void UpdatePlayerList()
        {
            if (!this.PlayerItemCreated)
            {
                this.CreateHostPlayerItem();
            }

            if (this.UIPlayerRows.Count < GameManager.Instance.LobbyPlayers.Count)
            {
                this.CreateClientPlayerItem();
            }

            if (this.UIPlayerRows.Count > GameManager.Instance.LobbyPlayers.Count)
            {
                this.RemovePlayerItem();
            }

            if (this.UIPlayerRows.Count == GameManager.Instance.LobbyPlayers.Count)
            {
                this.UpdatePlayerItem();
            }
        }

        public void CreateHostPlayerItem()
        {
            foreach (var lobbyPlayer in GameManager.Instance.LobbyPlayers)
            {
                var uiPlayerRow = Instantiate(this.UIPlayerLobbyPrefab);

                uiPlayerRow.PlayerName = lobbyPlayer.PlayerName;
                uiPlayerRow.ConnectionId = lobbyPlayer.ConnectionID;
                uiPlayerRow.PlayerSteamId = lobbyPlayer.PlayerSteamID;
                uiPlayerRow.IsReady = lobbyPlayer.IsReady;
                uiPlayerRow.SetPlayerValues();

                uiPlayerRow.transform.SetParent(this.PlayerLobbiesRT.transform);
                uiPlayerRow.transform.localScale = Vector3.one;

                this.UIPlayerRows.Add(uiPlayerRow);
            }

            this.PlayerItemCreated = true;
        }

        public void CreateClientPlayerItem()
        {
            foreach (var lobbyPlayer in GameManager.Instance.LobbyPlayers)
            {
                if (!this.UIPlayerRows.Any(b => b.ConnectionId == lobbyPlayer.ConnectionID))
                {
                    var uiPlayerRow = Instantiate(this.UIPlayerLobbyPrefab);

                    uiPlayerRow.PlayerName = lobbyPlayer.PlayerName;
                    uiPlayerRow.ConnectionId = lobbyPlayer.ConnectionID;
                    uiPlayerRow.PlayerSteamId = lobbyPlayer.PlayerSteamID;
                    uiPlayerRow.SetPlayerValues();

                    uiPlayerRow.transform.SetParent(this.PlayerLobbiesRT.transform);
                    uiPlayerRow.transform.localScale = Vector3.one;

                    this.UIPlayerRows.Add(uiPlayerRow);
                }
            }
        }

        public void UpdatePlayerItem()
        {
            foreach (var lobbyPlayer in GameManager.Instance.LobbyPlayers)
            {
                foreach (var uiPlayerRow in this.UIPlayerRows)
                {
                    if (uiPlayerRow.ConnectionId == lobbyPlayer.ConnectionID)
                    {
                        uiPlayerRow.PlayerName = lobbyPlayer.PlayerName;
                        uiPlayerRow.IsReady = lobbyPlayer.IsReady;
                        uiPlayerRow.SetPlayerValues();
                        if (lobbyPlayer == this.LocalPlayerController)
                        {
                            this.UpdateReadyButtonText();
                        }
                    }
                }
            }

            this.LobbyCountTMP.text = $"{this.UIPlayerRows.Count}/{GameManager.Instance.maxConnections}";
            this.CheckIfAllReady();
        }

        public void RemovePlayerItem()
        {
            var playerLobbiesToRemove = new List<UIPlayerRow>();

            foreach (var uiPlayerRow in this.UIPlayerRows)
            {
                if (!GameManager.Instance.LobbyPlayers.Any(b => b.ConnectionID == uiPlayerRow.ConnectionId))
                {
                    playerLobbiesToRemove.Add(uiPlayerRow);
                }
            }
            foreach (var toRemove in playerLobbiesToRemove)
            {
                this.UIPlayerRows.Remove(toRemove);
                if (toRemove != null)
                    Destroy(toRemove.gameObject);
            }
        }

        #region Ready logic
        public void ReadyPlayer()
        {
            this.LocalPlayerController.ChangeReady();
        }

        public void UpdateReadyButtonText()
        {
            this.ReadyTMP.text = this.LocalPlayerController.IsReady
                ? "Unready"
                : "Ready";
        }

        public void CheckIfAllReady()
        {
            var allReady = true;
            foreach (var player in GameManager.Instance.LobbyPlayers)
            {
                if (!player.IsReady)
                {
                    allReady = false;
                    break;
                }
            }

            if (allReady)
            {
                // Only the host
                if (this.LocalPlayerController.PlayerIdNumber == 0)
                {
                    this.StartButton.interactable = true;
                }
                else
                {
                    this.StartButton.interactable = false;
                }
            }
            else
            {
                this.StartButton.interactable = false;
            }
        }
        #endregion

        #region Buttons
        private void OnLeaveButtonClick()
        {
            SteamMatchmaking.LeaveLobby(SteamUser.GetSteamID());

            if (this.LocalPlayerController.ConnectionID == 0)
            {
                GameManager.Instance.StopHost();
            }
            else
            {
                GameManager.Instance.StopClient();
            }
        }

        private void OnReadyButtonClick()
        {
            this.LocalPlayerController.ChangeReady();
        }

        private void OnStartButtonClick()
        {
            GameManager.Instance.StartGame("ScGame");
        }
        #endregion
    }
}