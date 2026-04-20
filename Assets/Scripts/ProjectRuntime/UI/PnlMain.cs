using ProjectRuntime.Network;
using ProjectRuntime.Network.Steam;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class PnlMain : MonoBehaviour
    {
        public static PnlMain Instance { get; private set; }

        [field: SerializeField, Header("Scene References")]
        private Button PlayButton { get; set; }

        [field: SerializeField]
        private Button QuitButton { get; set; }

        [field: SerializeField]
        private Button HostButton { get; set; }

        [field: SerializeField]
        private Button BrowseLobbiesButton { get; set; }

        [field: SerializeField]
        private Button BackButton { get; set; }

        [field: SerializeField]
        private GameObject LobbySelectionObject { get; set; }

        [field: SerializeField]
        private PnlBrowseLobbies PnlBrowseLobbies { get; set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            this.PlayButton.onClick.AddListener(this.OnPlayButtonClick);
            this.QuitButton.onClick.AddListener(this.OnQuitButtonClick);
            this.HostButton.onClick.AddListener(this.OnHostButtonClick);
            this.BrowseLobbiesButton.onClick.AddListener(this.OnBrowseLobbiesButtonClick);
            this.BackButton.onClick.AddListener(this.OnBackButtonClick);

            this.ToggleCreateLobbyUI(false);
            this.ToggleMainUI(true);
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        private void OnPlayButtonClick()
        {
            this.ToggleCreateLobbyUI(true);
        }

        private void OnQuitButtonClick()
        {
            Application.Quit();
        }

        private void OnHostButtonClick()
        {
            SteamLobby.Instance.HostLobby();
        }

        private void OnBrowseLobbiesButtonClick()
        {
            this.ToggleMainUI(false);

            SteamLobby.Instance.GetLobbiesList();
        }

        private void OnBackButtonClick()
        {
            this.ToggleCreateLobbyUI(false);
        }

        private void ToggleCreateLobbyUI(bool toggle)
        {
            this.PlayButton.gameObject.SetActive(!toggle);
            this.QuitButton.gameObject.SetActive(!toggle);
            this.HostButton.gameObject.SetActive(toggle);
            this.BrowseLobbiesButton.gameObject.SetActive(toggle);
            this.BackButton.gameObject.SetActive(toggle);
        }

        public void ToggleMainUI(bool toggle)
        {
            this.LobbySelectionObject.SetActive(toggle);
            this.PnlBrowseLobbies.gameObject.SetActive(!toggle);
        }
    }
}