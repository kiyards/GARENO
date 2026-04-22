using ProjectRuntime.Network.Steam;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class PnlBrowseLobbies : MonoBehaviour
    {
        public static PnlBrowseLobbies Instance { get; private set; }

        [field: SerializeField, Header("Scene References")]
        public GameObject UIParent { get; private set; }

        [field: SerializeField]
        private RectTransform UILobbyRowRT { get; set; }

        [field: SerializeField]
        private Button BackButton { get; set; }

        [field: SerializeField]
        private Button RefreshButton { get; set; }

        [field: SerializeField, Header("Prefabs")]
        private UILobbyRow UILobbyRowPrefab { get; set; }

        // Accessible Variables
        public List<UILobbyRow> UILobbyRowList = new();

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            this.BackButton.onClick.AddListener(this.OnBackButtonClick);
            this.RefreshButton.onClick.AddListener(this.OnRefreshButtonClick);
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        private void OnBackButtonClick()
        {
            PnlMain.Instance.ToggleMainUI(true);
        }

        private void OnRefreshButtonClick()
        {
            this.ClearAllLobbies();
            SteamLobby.Instance.GetLobbiesList();
        }

        public void ClearAllLobbies()
        {
            foreach (var uiLobbyRow in this.UILobbyRowList)
            {
                Destroy(uiLobbyRow.gameObject);
            }
            this.UILobbyRowList.Clear();
        }

        public void DisplayLobbies(List<CSteamID> lobbyIds, LobbyDataUpdate_t callback)
        {
            for (var i = 0; i < lobbyIds.Count; i++)
            {
                if (lobbyIds[i].m_SteamID == callback.m_ulSteamIDLobby)
                {
                    var uiLobbyRow = this.UILobbyRowList.Find(row => row.LobbyId.m_SteamID == lobbyIds[i].m_SteamID);
                    if (uiLobbyRow == null)
                    {
                        uiLobbyRow = Instantiate(this.UILobbyRowPrefab, this.UILobbyRowRT);
                        uiLobbyRow.transform.localScale = Vector3.one;
                        uiLobbyRow.LobbyId = lobbyIds[i];
                        this.UILobbyRowList.Add(uiLobbyRow);
                    }

                    uiLobbyRow.LobbyName = SteamMatchmaking.GetLobbyData(lobbyIds[i], "name");
                    uiLobbyRow.LobbyCount = SteamMatchmaking.GetNumLobbyMembers(lobbyIds[i]);
                    uiLobbyRow.LobbySize = SteamMatchmaking.GetLobbyMemberLimit(lobbyIds[i]);
                    uiLobbyRow.SetLobbyData();
                    break;
                }
            }
        }
    }
}
