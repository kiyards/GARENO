using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class UIPlayerRow : MonoBehaviour
    {
        [field: SerializeField, Header("Scene References")]
        private RawImage ProfileDisplayImage { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI PlayerNameTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI ReadyTMP { get; set; }

        // Accessible Variables
        public string PlayerName;
        public int ConnectionId;
        public ulong PlayerSteamId;
        public bool IsReady;

        // Internal Variables
        private bool _steamAvatarReceived;

        protected Callback<AvatarImageLoaded_t> ImageLoaded;

        private void Awake()
        {
            this.ImageLoaded = Callback<AvatarImageLoaded_t>.Create(this.OnImageLoaded);
        }

        private void OnImageLoaded(AvatarImageLoaded_t callback)
        {
            if (callback.m_steamID.m_SteamID == this.PlayerSteamId)
            {
                this.ProfileDisplayImage.texture = this.GetSteamImageAsTexture(callback.m_iImage);
            }
        }

        private Texture2D GetSteamImageAsTexture(int iImage)
        {
            Texture2D texture = null;

            var isValid = SteamUtils.GetImageSize(iImage, out var width, out var height);
            if (isValid)
            {
                var image = new byte[width * height * 4];
                isValid = SteamUtils.GetImageRGBA(iImage, image, (int)(width * height * 4));
                if (isValid)
                {
                    texture = new Texture2D((int)width, (int)height, TextureFormat.RGBA32, false, false);
                    texture.LoadRawTextureData(image);
                    texture.Apply();
                }
            }

            this._steamAvatarReceived = true;
            return texture;
        }

        private void GetPlayerIcon()
        {
            var imageID = SteamFriends.GetLargeFriendAvatar((CSteamID)this.PlayerSteamId);
            if (imageID == -1)
            {
                return;
            }

            this.ProfileDisplayImage.texture = this.GetSteamImageAsTexture(imageID);
        }

        public void SetPlayerValues()
        {
            this.PlayerNameTMP.text = this.PlayerName;
            if (!this._steamAvatarReceived)
            {
                this.GetPlayerIcon();
            }

            this.ChangePlayerReadyStatus();
        }

        private void ChangePlayerReadyStatus()
        {
            this.ReadyTMP.text = this.IsReady
                ? "<color=green>Ready"
                : "<color=red>Not Ready";
        }
    }
}