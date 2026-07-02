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
        private TextMeshProUGUI DungeonMasterObjectiveTMP { get; set; }

        [field: SerializeField]
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
        private UIDungeonMasterHand DungeonMasterHand { get; set; }

        [field: SerializeField]
        private GameObject ManaBarParent { get; set; }

        [field: SerializeField]
        private GameObject CardDescription { get; set; }

        [field: SerializeField, Header("Minimap")]
        private MinimapController Minimap { get; set; }

        [field: SerializeField, Header("Direction Indicators")]
        private WorldDirectionIndicatorController DirectionIndicators { get; set; }

        [field: SerializeField, Header("Nemesis")]
        private Button NemesisButton { get; set; }

        [field: SerializeField]
        private TextMeshProUGUI NemesisCountdownTMP { get; set; }

        [field: SerializeField]
        private GameObject NemesisDarkenOverlay { get; set; }

        [field: SerializeField]
        private Image NemesisChargeFill { get; set; }

        [field: SerializeField]
        private GameObject NemesisControlUI { get; set; }

        private Health BoundHealth { get; set; }
        private PistolWeapon BoundWeapon { get; set; }
        private DungeonMasterCardManager BoundCardManager { get; set; }
        private GameplayPlayer BoundGameplayPlayer { get; set; }
        private BattleManager BoundBattleManager { get; set; }

        private PlayerManager LocalPlayer { get; set; }
        private PlayerRole CurrentRole { get; set; } = PlayerRole.Unassigned;
        private bool IsPlayerUiVisible { get; set; } = true;

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
            if (this.NemesisButton == null || this.BoundCardManager == null
                || this.CurrentRole != PlayerRole.DungeonMaster)
            {
                return;
            }

            bool available = this.BoundCardManager.NemesisAvailable;
            bool active = this.BoundCardManager.NemesisActive;

            // While the Nemesis entity exists the DM is actively controlling it; once it's gone
            // (lifetime expired or ended early) the one-use side-card is spent.
            var nemesisController =
                this.BoundGameplayPlayer != null ? this.BoundGameplayPlayer.Nemesis : null;
            bool controlling = nemesisController != null && nemesisController.HasActiveNemesis;

            this.NemesisButton.interactable = available && !active;

            if (this.NemesisDarkenOverlay != null)
            {
                this.NemesisDarkenOverlay.SetActive(!available || active);
            }

            if (this.NemesisCountdownTMP != null)
            {
                if (controlling)
                {
                    int lifeSeconds = Mathf.CeilToInt(nemesisController.ActiveLifetimeRemaining);
                    this.NemesisCountdownTMP.text = $"{lifeSeconds / 60}:{lifeSeconds % 60:00}";
                }
                else if (active)
                {
                    this.NemesisCountdownTMP.text = "USED";
                }
                else if (available)
                {
                    this.NemesisCountdownTMP.text = "READY";
                }
                else
                {
                    int seconds = Mathf.CeilToInt(this.BoundCardManager.NemesisRemainingSeconds);
                    this.NemesisCountdownTMP.text = $"{seconds / 60}:{seconds % 60:00}";
                }
            }

            // Charge fill: fills toward READY while charging, sits full when READY, drains over the
            // lifetime while controlling, and empties once spent.
            if (this.NemesisChargeFill != null)
            {
                float fill;
                if (controlling)
                {
                    fill = nemesisController.ActiveLifetimeFraction;
                }
                else if (active)
                {
                    fill = 0f;
                }
                else if (available)
                {
                    fill = 1f;
                }
                else
                {
                    fill = this.BoundCardManager.NemesisReadyProgress;
                }

                this.NemesisChargeFill.fillAmount = fill;
            }

            // Entering/leaving Nemesis control flips whether the normal DM HUD is shown. Re-apply HUD
            // visibility only on the transition so it isn't fought every frame.
            if (controlling != this._wasControllingNemesis)
            {
                this._wasControllingNemesis = controlling;
                this.ApplyHudVisibility();
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
            if (TurretReticle != null)
                TurretReticle.SetActive(active);
            if (TurretAmmoParent != null)
                TurretAmmoParent.SetActive(active);
            if (TurretLifetimeParent != null)
                TurretLifetimeParent.SetActive(active);
            if (TurretDisassemblingParent != null)
                TurretDisassemblingParent.SetActive(false);
            if (DungeonMasterHand != null)
                DungeonMasterHand.gameObject.SetActive(!active);
            if (ManaBarParent != null)
                ManaBarParent.SetActive(!active);
            if (CardDescription != null)
                CardDescription.SetActive(!active);
        }

        public void SetTurretAmmo(int current, int max)
        {
            if (TurretCurrentAmmoTMP != null)
                TurretCurrentAmmoTMP.text = $"{current}/{max}";
        }

        public void SetTurretAmmoActive(bool active)
        {
            if (TurretAmmoParent != null)
                TurretAmmoParent.SetActive(active);
        }

        public void SetTurretLifetime(float remaining, float max)
        {
            if (TurretLifetimeBarFill != null && max > 0f)
                TurretLifetimeBarFill.fillAmount = remaining / max;
            if (TurretLifetimeTMP != null)
                TurretLifetimeTMP.text = Mathf.CeilToInt(remaining).ToString();
        }

        public void SetTurretLifetimeActive(bool active)
        {
            if (TurretLifetimeParent != null)
                TurretLifetimeParent.SetActive(active);
        }

        public void SetTurretDisassembling(float fill)
        {
            if (TurretDisassemblingBarFill != null)
                TurretDisassemblingBarFill.fillAmount = fill;
        }

        public void SetTurretDisassemblingActive(bool active)
        {
            if (TurretDisassemblingParent != null)
                TurretDisassemblingParent.SetActive(active);
        }

        public void SetTurretReticleActive(bool active)
        {
            if (TurretReticle != null)
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
                this.EnsureMinimap()?.SetBattleManager(null);
                this.EnsureDirectionIndicators()?.SetBattleManager(null);
                this.RefreshTimerText();
                this.RefreshObjectiveText();
                return;
            }

            this.BoundBattleManager = battleManager;
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
            this.RefreshRoleMessage();
            this.RefreshTimerText();
            this.RefreshObjectiveText();
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
                this.ObjectiveTMP.text = this.ComposeObjectiveText("Crystals: 0/0");
                return;
            }

            var objectiveText = battleManager.CurrentRoundPhase switch
            {
                RoundPhase.CrystalsComplete =>
                    $"Extract: {battleManager.ExtractedSurvivors}/{battleManager.RequiredExtractedSurvivors}",
                RoundPhase.RoundComplete => string.Empty,
                _ =>
                    $"Crystals: {battleManager.DestroyedCrystals}/{battleManager.RequiredCrystals}",
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
            int remaining = battleManager != null
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

            this.DirectionIndicators = GetComponentInChildren<WorldDirectionIndicatorController>(true);
            if (this.DirectionIndicators == null)
            {
                this.DirectionIndicators = gameObject.AddComponent<WorldDirectionIndicatorController>();
            }

            return this.DirectionIndicators;
        }
    }
}
