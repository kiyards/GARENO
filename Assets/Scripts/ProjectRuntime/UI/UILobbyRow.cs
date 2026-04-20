using ProjectRuntime.Network.Steam;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class UILobbyRow : MonoBehaviour
    {
        [field: SerializeField, Header("Scene References")]
        private TextMeshProUGUI LobbyNameTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI LobbyCountTMP { get; set; }

        [field: SerializeField]
        private Button JoinButton { get; set; }

        // Accessible Variables
        public CSteamID LobbyId;
        public string LobbyName;
        public int LobbyCount;
        public int LobbySize;

        private void Awake()
        {
            this.JoinButton.onClick.AddListener(this.JoinLobby);
        }

        public void SetLobbyData()
        {
            this.LobbyNameTMP.text = string.IsNullOrEmpty(this.LobbyName)
                ? "Untitled Lobby"
                : this.LobbyName;

            this.LobbyCountTMP.text = $"{this.LobbyCount}/{this.LobbySize}";
        }

        private void JoinLobby()
        {
            SteamLobby.Instance.JoinLobby(this.LobbyId);
        }
    }
}