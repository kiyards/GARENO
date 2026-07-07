using System.Collections;
using ProjectRuntime.Actor;
using ProjectRuntime.Combat;
using ProjectRuntime.Network;
using ProjectRuntime.UI;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace ProjectRuntime.Managers
{
    /// <summary>
    /// HUD logic only. The canvas and all widgets are authored in the scene/prefab and
    /// wired into the serialized fields below — this class subscribes to gameplay events
    /// and pushes values into those references. It does not build any UI at runtime.
    /// </summary>
    public class PlayerHudManager : MonoBehaviour
    {
        public static PlayerHudManager Instance { get; private set; }

        [field: SerializeField, Header("Scene References")]
        private GameObject SharedUIParent { get; set; }

        [field: SerializeField]
        private GameObject SurvivorOnlyUIParent { get; set; }

        [field: SerializeField]
        [field: FormerlySerializedAs("<MastermindOnlyUIParent>k__BackingField")]
        private GameObject DungeonMasterOnlyUIParent { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI RoleMessageTMP { get; set; }

        [field: SerializeField, Header("Player Health")]
        private FlashFillBar PlayerHealthBar { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI PlayerHealthTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI SurvivorCurrentAmmoTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI SurvivorSpareAmmoTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI ObjectiveTMP { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI TimerTMP { get; set; }

        [field: SerializeField, Header("Dungeon Master")]
        private Image ManaBarFill { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI ManaTMP { get; set; }

        [field: SerializeField]
        private GameObject ManaBarParent { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI DungeonMasterObjectiveTMP { get; set; }

        [field: SerializeField]
        private UIDungeonMasterHand DungeonMasterHand { get; set; }

        [field: SerializeField]
        private GameObject CardDescription { get; set; }

        [field: SerializeField, Header("Turret")]
        private GameObject TurretReticle { get; set; }

        [field: SerializeField]
        private GameObject TurretAmmoParent { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI TurretCurrentAmmoTMP { get; set; }

        [field: SerializeField]
        private GameObject TurretLifetimeParent { get; set; }

        [field: SerializeField]
        private Image TurretLifetimeBarFill { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI TurretLifetimeTMP { get; set; }

        [field: SerializeField]
        private GameObject TurretDisassemblingParent { get; set; }

        [field: SerializeField]
        private Image TurretDisassemblingBarFill { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI TurretEarlyCancelTMP { get; set; }

        [field: SerializeField, Header("Bear Trap Escape")]
        private GameObject BearTrapEscapeParent { get; set; }

        [field: SerializeField]
        private Image BearTrapEscapeBarFill { get; set; }

        [field: SerializeField]
        private RectTransform BearTrapEscapeBarTransform { get; set; }

        [field: SerializeField]
        private float BearTrapShakeMaxPixels { get; set; } = 12f;

        [field: SerializeField, Header("Revive")]
        private GameObject ReviveInteractParent { get; set; }

        [field: SerializeField]
        private Image ReviveInteractBarFill { get; set; }

        [field: SerializeField, Header("Revive Timer")]
        private GameObject ReviveTimerParent { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI ReviveTimerTMP { get; set; }

        [field: SerializeField, Header("Downed")]
        private GameObject DownedIndicator { get; set; }

        [field: SerializeField, Header("Crystal Destroyed Notification")]
        private Image CrystalDestroyedNotification { get; set; }

        [field: SerializeField]
        private float CrystalNotificationHoldSeconds { get; set; } = 1f;

        [field: SerializeField]
        private float CrystalNotificationFadeSeconds { get; set; } = 1.5f;

        [field: SerializeField, Header("Minimap")]
        private MinimapController Minimap { get; set; }

        [field: SerializeField, Header("Direction Indicators")]
        private WorldDirectionIndicatorController DirectionIndicators { get; set; }

        [field: SerializeField, Header("Nemesis")]
        private GameObject NemesisParent { get; set; }

        [field: SerializeField]
        private Button NemesisButton { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI NemesisCountdownTMP { get; set; }

        [field: SerializeField]
        private GameObject NemesisDarkenOverlay { get; set; }

        [field: SerializeField]
        private Image NemesisChargeFill { get; set; }

        [field: SerializeField]
        private GameObject NemesisControlUI { get; set; }

        // Fixed 3-ability set (Punch/Lunge/Ground Slam), placed under NemesisControlUI. Each instance
        // self-identifies via UIAttackDisplay.Type, so no dynamic slot-assignment is needed.
        [field: SerializeField]
        private UIAttackDisplay[] NemesisAttackDisplays { get; set; }

        // Live lifetime countdown shown inside NemesisControlUI while controlling the Nemesis (the
        // side-card's NemesisCountdownTMP/NemesisChargeFill are hidden with the DM HUD in that state).
        [field: SerializeField]
        private TextMeshProUGUI NemesisControlCountdownTMP { get; set; }

        [field: SerializeField]
        private Image NemesisControlLifetimeFill { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI NemesisEarlyCancelTMP { get; set; }

        [field: SerializeField, Header("Effects")]
        private FlashEffect FlashEffect { get; set; }

        private Health BoundHealth { get; set; }
        private PistolWeapon BoundWeapon { get; set; }
        private DungeonMasterCardManager BoundCardManager { get; set; }
        private GameplayPlayer BoundGameplayPlayer { get; set; }
        private BattleManager BoundBattleManager { get; set; }

        private PlayerManager LocalPlayer { get; set; }
        private PlayerRole CurrentRole { get; set; } = PlayerRole.Unassigned;
        private bool IsPlayerUiVisible { get; set; } = true;
        private float _reviveHoldProgress;
        private int _lastDestroyedCrystals;
        private Coroutine _crystalNotificationRoutine;

        /// <summary>
        /// Returns the scene-authored HUD instance. The HUD must exist in the active scene
        /// (added via the PlayerHUD prefab); this never constructs one at runtime.
        /// </summary>
        public static PlayerHudManager EnsureInstance()
        {
            if (Instance != null)
            {
                return Instance;
            }

            var existing = FindFirstObjectByType<PlayerHudManager>(FindObjectsInactive.Include);
            if (existing != null)
            {
                return existing;
            }

            Debug.LogError(
                "[PlayerHudManager] No HUD found in the active scene. Add the PlayerHUD prefab to ScGame."
            );
            return null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this.gameObject);
                return;
            }

            Instance = this;
            this.EnsureMinimap();
            this.EnsureDirectionIndicators();
            this.SetRole(PlayerRole.Unassigned);
        }

        private void OnDestroy()
        {
            this.UnbindCombat();
            this.UnbindBattleManager();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (this.BoundBattleManager == null && BattleManager.Instance != null)
            {
                this.BindBattleManager(BattleManager.Instance);
            }

            this.RefreshNemesisSideCard();
            this.RefreshNemesisAttackDisplays();
            this.RefreshReviveInteract();
            this.RefreshReviveTimer();
            this.RefreshDownedIndicator();
        }

        public void SetLocalPlayer(PlayerManager player)
        {
            this.LocalPlayer = player;
            this.EnsureMinimap()?.BindLocalPlayer(player);
            this.EnsureDirectionIndicators()?.BindLocalPlayer(player);
            this.SetRole(player != null ? player.playerRole : PlayerRole.Unassigned);
            this.BindCombat(player != null ? player.player : null);
        }

        private void BindCombat(GameplayPlayer gameplayPlayer)
        {
            this.UnbindCombat();
            if (gameplayPlayer == null)
            {
                return;
            }

            this.BoundGameplayPlayer = gameplayPlayer;
            gameplayPlayer.SetFlashEffect(this.FlashEffect);

            this.BoundHealth = gameplayPlayer.health;
            if (this.BoundHealth != null)
            {
                this.BoundHealth.OnHealthChangedEvent += this.OnHealthChanged;
                this.OnHealthChanged(this.BoundHealth.CurrentHealth, this.BoundHealth.MaxHealth);
            }

            this.BoundWeapon = gameplayPlayer.GetComponent<PistolWeapon>();
            if (this.BoundWeapon != null)
            {
                this.BoundWeapon.OnAmmoChangedEvent += this.OnAmmoChanged;
                this.OnAmmoChanged(this.BoundWeapon.CurrentAmmo, this.BoundWeapon.MagazineSize);
            }

            this.BoundCardManager = gameplayPlayer.CardManager;
            if (this.BoundCardManager != null)
            {
                this.BoundCardManager.OnManaChangedEvent += this.OnManaChanged;
                this.OnManaChanged(this.BoundCardManager.Mana, this.BoundCardManager.MaxMana);
                this.DungeonMasterHand?.Bind(this.BoundCardManager);
            }

            if (this.NemesisButton != null)
            {
                this.NemesisButton.onClick.AddListener(this.OnNemesisButtonClicked);
            }

            this.RefreshNemesisSideCard();
        }

        private void UnbindCombat()
        {
            if (this.BoundHealth != null)
            {
                this.BoundHealth.OnHealthChangedEvent -= this.OnHealthChanged;
                this.BoundHealth = null;
            }

            if (this.BoundWeapon != null)
            {
                this.BoundWeapon.OnAmmoChangedEvent -= this.OnAmmoChanged;
                this.BoundWeapon = null;
            }

            if (this.BoundCardManager != null)
            {
                this.BoundCardManager.OnManaChangedEvent -= this.OnManaChanged;
                this.DungeonMasterHand?.Unbind();
                this.BoundCardManager = null;
            }

            if (this.NemesisButton != null)
            {
                this.NemesisButton.onClick.RemoveListener(this.OnNemesisButtonClicked);
            }

            this.BoundGameplayPlayer?.SetFlashEffect(null);
            this.BoundGameplayPlayer = null;
        }

        private void OnNemesisButtonClicked()
        {
            // Clicking the side-card begins client-side placement targeting; the Nemesis spawns once the
            // Dungeon Master confirms a ground position (see DungeonMasterCardManager.TryBeginNemesisPlacement).
            if (this.BoundCardManager != null)
            {
                this.BoundCardManager.TryBeginNemesisPlacement();
            }
        }

        private void RefreshNemesisSideCard()
        {
            if (
                this.NemesisButton == null
                || this.BoundCardManager == null
                || this.CurrentRole != PlayerRole.DungeonMaster
            )
            {
                return;
            }

            bool available = this.BoundCardManager.NemesisAvailable;
            bool active = this.BoundCardManager.NemesisActive;

            // While the Nemesis entity exists the DM controls it and this side-card is hidden along
            // with the rest of the DM HUD — the live lifetime countdown shows in NemesisControlUI
            // instead (see RefreshNemesisControlLifetime).
            var nemesisController =
                this.BoundGameplayPlayer != null ? this.BoundGameplayPlayer.Nemesis : null;
            bool controlling = nemesisController != null && nemesisController.HasActiveNemesis;

            this.NemesisButton.interactable = available && !active;

            if (this.NemesisDarkenOverlay != null)
            {
                this.NemesisDarkenOverlay.SetActive(!available || active);
            }

            // Side-card tracks the cooldown until the Nemesis can next be deployed: "READY"/full when
            // available, otherwise the countdown to ready. (Not one-time-use — the countdown restarts
            // each time a deployed Nemesis ends; see DungeonMasterCardManager.ServerOnNemesisEnded.)
            if (this.NemesisCountdownTMP != null)
            {
                if (available)
                {
                    this.NemesisCountdownTMP.text = "READY";
                }
                else
                {
                    int seconds = Mathf.CeilToInt(this.BoundCardManager.NemesisRemainingSeconds);
                    this.NemesisCountdownTMP.text = $"{seconds / 60}:{seconds % 60:00}";
                }
            }

            if (this.NemesisChargeFill != null)
            {
                this.NemesisChargeFill.fillAmount =
                    available ? 1f : this.BoundCardManager.NemesisReadyProgress;
            }

            // Entering/leaving Nemesis control flips whether the normal DM HUD is shown. Re-apply HUD
            // visibility only on the transition so it isn't fought every frame.
            if (controlling != this._wasControllingNemesis)
            {
                this._wasControllingNemesis = controlling;
                this.ApplyHudVisibility();
            }
        }

        private void RefreshNemesisAttackDisplays()
        {
            var nemesisController =
                this.BoundGameplayPlayer != null ? this.BoundGameplayPlayer.Nemesis : null;
            var nemesis = nemesisController != null ? nemesisController.ActiveNemesis : null;

            this.RefreshNemesisControlLifetime(nemesisController);

            if (this.NemesisAttackDisplays == null || this.NemesisAttackDisplays.Length == 0)
            {
                return;
            }

            foreach (var display in this.NemesisAttackDisplays)
            {
                if (display == null)
                {
                    continue;
                }

                if (nemesis == null)
                {
                    display.SetAvailable(false);
                    display.SetCooldownFill(0f);
                    continue;
                }

                display.SetCooldownFill(nemesis.GetAttackCooldownFraction(display.Type));
                display.SetAvailable(nemesis.IsAttackAvailable(display.Type));
            }
        }

        // Shows the revive prompt while the local survivor is within range of a downed teammate, and
        // fills it as Interact is held. This is a client-side approximation for feedback only — the
        // authoritative hold timer lives on the downed player (see GameplayPlayer.ServerRegisterReviveContact).
        private void RefreshReviveInteract()
        {
            if (this.BoundGameplayPlayer == null || this.CurrentRole != PlayerRole.Survivor)
            {
                this._reviveHoldProgress = 0f;
                this.SetReviveInteractActive(false);
                return;
            }

            var target = this.BoundGameplayPlayer.FindReviveTarget();
            if (target == null)
            {
                this._reviveHoldProgress = 0f;
                this.SetReviveInteractActive(false);
                return;
            }

            this.SetReviveInteractActive(true);

            bool holding =
                this.BoundGameplayPlayer.input != null && this.BoundGameplayPlayer.input.InteractHold;
            float holdTime = this.BoundGameplayPlayer.ReviveHoldTime;
            this._reviveHoldProgress = holding
                ? Mathf.Min(this._reviveHoldProgress + Time.deltaTime, holdTime)
                : 0f;

            if (this.ReviveInteractBarFill != null)
            {
                this.ReviveInteractBarFill.fillAmount =
                    holdTime > 0f ? this._reviveHoldProgress / holdTime : 0f;
            }
        }

        private void SetReviveInteractActive(bool active)
        {
            if (this.ReviveInteractParent != null)
                this.ReviveInteractParent.SetActive(active);
        }

        // Shows the local downed survivor's own countdown until the revive window expires and they're
        // permanently lost. Backed by GameplayPlayer.DownedTimeRemaining, which reads DownedState's
        // replicated totalDuration/elapsedTime — no separate client-side timer to keep in sync.
        private void RefreshReviveTimer()
        {
            if (this.BoundGameplayPlayer == null || !this.BoundGameplayPlayer.IsDowned)
            {
                this.SetReviveTimerActive(false);
                return;
            }

            this.SetReviveTimerActive(true);

            float remaining = this.BoundGameplayPlayer.DownedTimeRemaining;
            this.ReviveTimerTMP.text = $"{remaining:0.0}s";
        }

        private void SetReviveTimerActive(bool active)
        {
            this.ReviveTimerParent.SetActive(active);
        }

        // Shown while the local survivor is downed (awaiting revive or bleed-out), hidden while alive.
        // Backed by the same GameplayPlayer.IsDowned state that drives the revive timer above.
        private void RefreshDownedIndicator()
        {
            bool downed = this.BoundGameplayPlayer != null && this.BoundGameplayPlayer.IsDowned;
            this.DownedIndicator.SetActive(downed);
        }

        // Drives the lifetime countdown text + bar inside NemesisControlUI (only visible while
        // controlling). The fill drains 1 → 0 as the Nemesis's lifetime elapses.
        private void RefreshNemesisControlLifetime(DungeonMasterNemesisController nemesisController)
        {
            if (nemesisController == null || !nemesisController.HasActiveNemesis)
            {
                return;
            }

            if (this.NemesisControlLifetimeFill != null)
            {
                this.NemesisControlLifetimeFill.fillAmount = nemesisController.ActiveLifetimeFraction;
            }

            if (this.NemesisControlCountdownTMP != null)
            {
                int seconds = Mathf.CeilToInt(nemesisController.ActiveLifetimeRemaining);
                this.NemesisControlCountdownTMP.text = $"{seconds / 60}:{seconds % 60:00}";
            }
        }

        private void OnHealthChanged(float current, float max)
        {
            if (this.PlayerHealthBar != null && max > 0f)
            {
                this.PlayerHealthBar.FillAmount = current / max;
            }

            if (this.PlayerHealthTMP != null)
            {
                this.PlayerHealthTMP.text = Mathf.CeilToInt(Mathf.Max(0f, current)).ToString();
            }
        }

        private void OnAmmoChanged(int currentAmmo, int magazineSize)
        {
            this.SurvivorCurrentAmmoTMP.text = $"{currentAmmo}";
            this.SurvivorSpareAmmoTMP.text = "∞";
        }

        private void OnManaChanged(float current, int max)
        {
            if (this.ManaBarFill != null && max > 0)
            {
                this.ManaBarFill.fillAmount = current / max;
            }

            if (this.ManaTMP != null)
            {
                this.ManaTMP.text = Mathf.FloorToInt(Mathf.Max(0f, current)).ToString();
            }
        }

        public void SetRole(PlayerRole role)
        {
            this.CurrentRole = role;
            this.EnsureMinimap()?.SetRole(role);
            this.EnsureDirectionIndicators()?.SetRole(role);

            this.ApplyHudVisibility();
            this.BindBattleManager(BattleManager.Instance);
            this.RefreshRoleMessage();
            this.RefreshTimerText();
            this.RefreshObjectiveText();
        }

        public void SetTurretModeActive(bool active)
        {
            TurretReticle.SetActive(active);
            TurretAmmoParent.SetActive(active);
            TurretLifetimeParent.SetActive(active);
            TurretDisassemblingParent.SetActive(false);
            if (DungeonMasterHand != null)
                DungeonMasterHand.gameObject.SetActive(!active);
            if (ManaBarParent != null)
                ManaBarParent.SetActive(!active);
            if (CardDescription != null)
                CardDescription.SetActive(!active);
            if (NemesisParent != null)
                NemesisParent.SetActive(!active);
            TurretEarlyCancelTMP.gameObject.SetActive(active);
        }

        public void SetTurretAmmo(int current, int max)
        {
            TurretCurrentAmmoTMP.text = $"{current}/{max}";
        }

        public void SetTurretAmmoActive(bool active)
        {
            TurretAmmoParent.SetActive(active);
        }

        public void SetTurretLifetime(float remaining, float max)
        {
            if (max > 0f)
                TurretLifetimeBarFill.fillAmount = remaining / max;
            TurretLifetimeTMP.text = Mathf.CeilToInt(remaining).ToString();
        }

        public void SetTurretLifetimeActive(bool active)
        {
            TurretLifetimeParent.SetActive(active);
        }

        public void SetTurretDisassembling(float fill)
        {
            TurretDisassemblingBarFill.fillAmount = fill;
        }

        public void SetTurretDisassemblingActive(bool active)
        {
            TurretDisassemblingParent.SetActive(active);
        }

        public void SetBearTrapBarActive(bool active)
        {
            BearTrapEscapeParent.SetActive(active);
        }

        public void SetBearTrapEscapeFill(float fill)
        {
            BearTrapEscapeBarFill.fillAmount = fill;
        }

        public void TriggerBearTrapShake(float fillAmount)
        {
            StopCoroutine(nameof(BearTrapShakeCoroutine));
            StartCoroutine(BearTrapShakeCoroutine(fillAmount));
        }

        private IEnumerator BearTrapShakeCoroutine(float intensity)
        {
            float duration = 0.12f;
            float elapsed = 0f;
            Vector2 origin = BearTrapEscapeBarTransform.anchoredPosition;
            float maxOffset = BearTrapShakeMaxPixels * intensity;

            while (elapsed < duration)
            {
                float x = Random.Range(-maxOffset, maxOffset);
                float y = Random.Range(-maxOffset, maxOffset);
                BearTrapEscapeBarTransform.anchoredPosition = origin + new Vector2(x, y);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            BearTrapEscapeBarTransform.anchoredPosition = origin;
        }

        public void SetTurretReticleActive(bool active)
        {
            TurretReticle.SetActive(active);
        }

        public void TogglePlayerUI(bool toggle)
        {
            this.IsPlayerUiVisible = toggle;
            this.ApplyHudVisibility();
        }

        // True while the local Dungeon Master is actively controlling the Nemesis. The normal DM HUD
        // is hidden in this state so only the in-control overlay (NemesisControlUI) shows.
        private bool IsControllingNemesis =>
            this.BoundGameplayPlayer != null && this.BoundGameplayPlayer.Nemesis.HasActiveNemesis;

        private bool _wasControllingNemesis;

        private void ApplyHudVisibility()
        {
            this.EnsureMinimap()?.SetHudVisible(this.IsPlayerUiVisible);
            this.EnsureDirectionIndicators()?.SetHudVisible(this.IsPlayerUiVisible);

            if (
                this.SharedUIParent == null
                || this.SurvivorOnlyUIParent == null
                || this.DungeonMasterOnlyUIParent == null
            )
            {
                return;
            }

            this.SharedUIParent.SetActive(this.IsPlayerUiVisible);
            this.SurvivorOnlyUIParent.SetActive(
                this.IsPlayerUiVisible && this.CurrentRole == PlayerRole.Survivor
            );
            this.DungeonMasterOnlyUIParent.SetActive(
                this.IsPlayerUiVisible
                    && this.CurrentRole == PlayerRole.DungeonMaster
                    && !this.IsControllingNemesis
            );

            // The in-control overlay sits on the always-active canvas root (not a role parent), so it
            // must be explicitly hidden for everyone except the Dungeon Master while controlling —
            // otherwise it would show on survivors' screens (its prefab default is active).
            if (this.NemesisControlUI != null)
            {
                this.NemesisControlUI.SetActive(
                    this.IsPlayerUiVisible
                        && this.CurrentRole == PlayerRole.DungeonMaster
                        && this.IsControllingNemesis
                );
            }

            if (this.NemesisEarlyCancelTMP != null)
                this.NemesisEarlyCancelTMP.gameObject.SetActive(
                    this.IsPlayerUiVisible
                        && this.CurrentRole == PlayerRole.DungeonMaster
                        && this.IsControllingNemesis
                );
        }

        private void BindBattleManager(BattleManager battleManager)
        {
            if (this.BoundBattleManager == battleManager)
            {
                return;
            }

            this.UnbindBattleManager();

            if (battleManager == null)
            {
                this._lastDestroyedCrystals = 0;
                this.EnsureMinimap()?.SetBattleManager(null);
                this.EnsureDirectionIndicators()?.SetBattleManager(null);
                this.RefreshTimerText();
                this.RefreshObjectiveText();
                return;
            }

            this.BoundBattleManager = battleManager;
            this._lastDestroyedCrystals = battleManager.DestroyedCrystals;
            this.EnsureMinimap()?.SetBattleManager(battleManager);
            this.EnsureDirectionIndicators()?.SetBattleManager(battleManager);
            this.BoundBattleManager.OnRoundStateChanged += this.OnRoundStateChanged;
            this.RefreshTimerText();
            this.RefreshObjectiveText();
        }

        private void UnbindBattleManager()
        {
            if (this.BoundBattleManager == null)
            {
                return;
            }

            this.BoundBattleManager.OnRoundStateChanged -= this.OnRoundStateChanged;
            this.BoundBattleManager = null;
        }

        private void OnRoundStateChanged()
        {
            this.CheckCrystalDestroyedNotification();
            this.RefreshRoleMessage();
            this.RefreshTimerText();
            this.RefreshObjectiveText();
        }

        // OnRoundStateChanged fires for any objective/timer sync, so compare against the last-seen
        // destroyed count and only flash the notification when a new crystal has actually gone down.
        // Fires on every client (survivor and Dungeon Master alike) since destroyedCrystals is a SyncVar.
        private void CheckCrystalDestroyedNotification()
        {
            var battleManager =
                this.BoundBattleManager != null ? this.BoundBattleManager : BattleManager.Instance;
            if (battleManager == null)
            {
                return;
            }

            int destroyed = battleManager.DestroyedCrystals;
            if (destroyed > this._lastDestroyedCrystals)
            {
                this.ShowCrystalDestroyedNotification();
            }

            this._lastDestroyedCrystals = destroyed;
        }

        // Restarts the hold-then-fade each time it's called, so a second crystal destroyed mid-fade
        // snaps the image back to full opacity and begins the timer again from the start.
        private void ShowCrystalDestroyedNotification()
        {
            if (this._crystalNotificationRoutine != null)
            {
                this.StopCoroutine(this._crystalNotificationRoutine);
            }

            this._crystalNotificationRoutine = this.StartCoroutine(
                this.CrystalDestroyedNotificationCoroutine()
            );
        }

        private IEnumerator CrystalDestroyedNotificationCoroutine()
        {
            var color = this.CrystalDestroyedNotification.color;
            color.a = 1f;
            this.CrystalDestroyedNotification.color = color;
            this.CrystalDestroyedNotification.gameObject.SetActive(true);

            yield return new WaitForSecondsRealtime(this.CrystalNotificationHoldSeconds);

            float elapsed = 0f;
            while (elapsed < this.CrystalNotificationFadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                color.a = Mathf.Clamp01(1f - elapsed / this.CrystalNotificationFadeSeconds);
                this.CrystalDestroyedNotification.color = color;
                yield return null;
            }

            color.a = 0f;
            this.CrystalDestroyedNotification.color = color;
            this.CrystalDestroyedNotification.gameObject.SetActive(false);
        }

        private void RefreshObjectiveText()
        {
            this.RefreshDungeonMasterObjectiveText();

            if (this.ObjectiveTMP == null)
            {
                return;
            }

            if (this.CurrentRole != PlayerRole.Survivor)
            {
                this.ObjectiveTMP.text = this.IsTimerComposedWithObjectiveText()
                    ? this.GetFormattedTimerText()
                    : string.Empty;
                return;
            }

            var battleManager =
                this.BoundBattleManager != null ? this.BoundBattleManager : BattleManager.Instance;

            if (battleManager == null)
            {
                this.ObjectiveTMP.text = this.ComposeObjectiveText("Destroy the Crystals: 0/0");
                return;
            }

            var objectiveText = battleManager.CurrentRoundPhase switch
            {
                RoundPhase.CrystalsComplete =>
                    $"Extract yourselves: {battleManager.ExtractedSurvivors}/{battleManager.RequiredExtractedSurvivors}",
                RoundPhase.RoundComplete => string.Empty,
                _ =>
                    $"Destroy the Crystals: {battleManager.DestroyedCrystals}/{battleManager.RequiredCrystals}",
            };

            this.ObjectiveTMP.text = this.ComposeObjectiveText(objectiveText);
        }

        private void RefreshDungeonMasterObjectiveText()
        {
            if (this.DungeonMasterObjectiveTMP == null)
            {
                return;
            }

            var battleManager =
                this.BoundBattleManager != null ? this.BoundBattleManager : BattleManager.Instance;

            int required = battleManager != null ? battleManager.RequiredCrystals : 3;
            int remaining =
                battleManager != null
                    ? Mathf.Max(0, required - battleManager.DestroyedCrystals)
                    : required;

            this.DungeonMasterObjectiveTMP.text = $"Protect the Crystals ({remaining}/{required})";
        }

        private void RefreshTimerText()
        {
            if (this.TimerTMP == null || this.IsTimerComposedWithObjectiveText())
            {
                return;
            }

            this.TimerTMP.text = this.GetFormattedTimerText();
        }

        private string ComposeObjectiveText(string objectiveText)
        {
            if (!this.IsTimerComposedWithObjectiveText())
            {
                return objectiveText;
            }

            if (string.IsNullOrEmpty(objectiveText))
            {
                return this.GetFormattedTimerText();
            }

            return $"{this.GetFormattedTimerText()} | {objectiveText}";
        }

        private string GetFormattedTimerText()
        {
            var battleManager =
                this.BoundBattleManager != null ? this.BoundBattleManager : BattleManager.Instance;

            if (battleManager == null)
            {
                return "--:--";
            }

            int remainingSeconds = Mathf.Max(0, battleManager.RemainingRoundSeconds);
            return $"{remainingSeconds / 60}:{remainingSeconds % 60:00}";
        }

        private bool IsTimerComposedWithObjectiveText()
        {
            return this.TimerTMP != null && this.TimerTMP == this.ObjectiveTMP;
        }

        private void RefreshRoleMessage()
        {
            if (this.RoleMessageTMP == null)
            {
                return;
            }

            var battleManager =
                this.BoundBattleManager != null ? this.BoundBattleManager : BattleManager.Instance;

            if (
                battleManager != null
                && battleManager.CurrentRoundPhase == RoundPhase.RoundComplete
            )
            {
                this.RoleMessageTMP.text = battleManager.Winner switch
                {
                    RoundWinner.Survivors => "Survivors Win",
                    RoundWinner.DungeonMaster => "Dungeon Master Wins",
                    _ => "Round Complete",
                };
                return;
            }

            var roleText =
                this.CurrentRole == PlayerRole.Unassigned
                    ? "Assigning role..."
                    : $"You are a {GetRoleDisplayName(this.CurrentRole)}";

            if (this.ShouldComposeTimerWithRoleMessage())
            {
                roleText = $"{this.GetFormattedTimerText()} | {roleText}";
            }

            this.RoleMessageTMP.text = roleText;
        }

        private bool ShouldComposeTimerWithRoleMessage()
        {
            return this.CurrentRole == PlayerRole.DungeonMaster
                && this.IsTimerComposedWithObjectiveText();
        }

        private static string GetRoleDisplayName(PlayerRole role)
        {
            return role == PlayerRole.DungeonMaster ? "Dungeon Master" : role.ToString();
        }

        private MinimapController EnsureMinimap()
        {
            if (this.Minimap != null)
            {
                return this.Minimap;
            }

            this.Minimap = GetComponentInChildren<MinimapController>(true);
            if (this.Minimap == null)
            {
                this.Minimap = gameObject.AddComponent<MinimapController>();
            }

            return this.Minimap;
        }

        private WorldDirectionIndicatorController EnsureDirectionIndicators()
        {
            if (this.DirectionIndicators != null)
            {
                return this.DirectionIndicators;
            }

            this.DirectionIndicators = GetComponentInChildren<WorldDirectionIndicatorController>(
                true
            );
            if (this.DirectionIndicators == null)
            {
                this.DirectionIndicators =
                    gameObject.AddComponent<WorldDirectionIndicatorController>();
            }

            return this.DirectionIndicators;
        }
    }
}
