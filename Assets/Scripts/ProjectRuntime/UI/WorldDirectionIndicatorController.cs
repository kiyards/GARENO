using System.Collections.Generic;
using ProjectRuntime.Actor;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using ProjectRuntime.Objectives;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    /// <summary>
    /// Survivor-only direction markers for gameplay goals. Shader-backed outlines handle
    /// through-wall visuals elsewhere; this component only shows HUD directions and distances.
    /// </summary>
    public class WorldDirectionIndicatorController : MonoBehaviour
    {
        [Header("Layout")]
        [SerializeField] private RectTransform indicatorRoot;
        [SerializeField] private float edgePadding = 48f;
        [SerializeField] private float markerWidth = 108f;
        [SerializeField] private float markerHeight = 32f;
        [SerializeField] private float refreshInterval = 0.2f;

        [Header("Colors")]
        [SerializeField] private Color teammateColor = new(0.35f, 0.8f, 1f, 0.9f);
        [SerializeField] private Color downedColor = new(1f, 0.35f, 0.2f, 0.95f);
        [SerializeField] private Color crystalColor = new(0.7f, 0.35f, 1f, 0.95f);
        [SerializeField] private Color extractionColor = new(0.1f, 1f, 0.25f, 0.95f);

        private readonly List<IndicatorTarget> _targets = new();
        private readonly Dictionary<int, IndicatorView> _views = new();
        private readonly HashSet<int> _visibleViews = new();

        private PlayerManager _localPlayer;
        private PlayerRole _role = PlayerRole.Unassigned;
        private BattleManager _battleManager;
        private Canvas _canvas;
        private Camera _uiCamera;
        private bool _hudVisible = true;
        private bool _showTeammates;
        private float _nextRefreshTime;

        private void Awake()
        {
            EnsureRoot();
        }

        private void Update()
        {
            if (ShouldReadToggleInput())
            {
                _showTeammates = !_showTeammates;
                RefreshTargets();
            }

            if (Time.unscaledTime >= _nextRefreshTime)
            {
                _nextRefreshTime = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
                RefreshTargets();
            }

            UpdateIndicators();
        }

        public void BindLocalPlayer(PlayerManager player)
        {
            _localPlayer = player;
            _showTeammates = false;
            RefreshTargets();
            ApplyVisibility();
        }

        public void SetRole(PlayerRole role)
        {
            _role = role;
            _showTeammates = false;
            RefreshTargets();
            ApplyVisibility();
        }

        public void SetBattleManager(BattleManager battleManager)
        {
            _battleManager = battleManager;
            RefreshTargets();
        }

        public void SetHudVisible(bool visible)
        {
            _hudVisible = visible;
            ApplyVisibility();
        }

        private bool ShouldReadToggleInput()
        {
            return _hudVisible &&
                   _role == PlayerRole.Survivor &&
                   _localPlayer != null &&
                   _localPlayer.player != null &&
                   _localPlayer.player.input != null &&
                   _localPlayer.player.input.TeammateIndicatorsTogglePress;
        }

        private void RefreshTargets()
        {
            _targets.Clear();

            var battleManager = _battleManager != null ? _battleManager : BattleManager.Instance;
            if (!_hudVisible ||
                _role != PlayerRole.Survivor ||
                _localPlayer == null ||
                _localPlayer.player == null ||
                battleManager == null)
            {
                return;
            }

            AddTeammateTargets(battleManager);
            AddObjectiveTargets(battleManager);
        }

        private void AddTeammateTargets(BattleManager battleManager)
        {
            foreach (var player in battleManager.Players)
            {
                if (player == null ||
                    player == _localPlayer ||
                    player.playerRole != PlayerRole.Survivor ||
                    player.player == null)
                {
                    continue;
                }

                if (player.player.IsDowned)
                {
                    AddTarget(player.player.transform, "DOWN", downedColor, 100);
                    continue;
                }

                if (_showTeammates && !player.player.IsInactive)
                {
                    AddTarget(player.player.transform, "TEAM", teammateColor, 110);
                }
            }
        }

        private void AddObjectiveTargets(BattleManager battleManager)
        {
            if (battleManager.CurrentRoundPhase == RoundPhase.DestroyCrystals &&
                battleManager.IsCrystalGuidanceRevealed)
            {
                foreach (var crystal in FindObjectsByType<CrystalObjective>(FindObjectsSortMode.None))
                {
                    if (crystal != null && !crystal.IsDespawned && crystal.IsGuidanceRevealed)
                    {
                        AddTarget(crystal.transform, "CRYSTAL", crystalColor, 200);
                    }
                }
            }

            if (battleManager.CurrentRoundPhase != RoundPhase.CrystalsComplete)
            {
                return;
            }

            foreach (var extractionZone in FindObjectsByType<ExtractionZone>(FindObjectsSortMode.None))
            {
                if (extractionZone != null)
                {
                    AddTarget(extractionZone.transform, "EXIT", extractionColor, 300);
                }
            }
        }

        private void AddTarget(Transform target, string label, Color color, int category)
        {
            if (target == null)
            {
                return;
            }

            _targets.Add(new IndicatorTarget
            {
                Id = target.GetInstanceID() ^ category,
                Target = target,
                Label = label,
                Color = color,
            });
        }

        private void UpdateIndicators()
        {
            EnsureRoot();

            if (indicatorRoot == null)
            {
                return;
            }

            Camera worldCamera = Camera.main;
            if (!_hudVisible ||
                _role != PlayerRole.Survivor ||
                _localPlayer == null ||
                _localPlayer.player == null ||
                worldCamera == null)
            {
                HideAll();
                return;
            }

            _visibleViews.Clear();
            foreach (var target in _targets)
            {
                if (target.Target == null)
                {
                    continue;
                }

                var view = GetOrCreateView(target.Id);
                _visibleViews.Add(target.Id);
                view.Root.gameObject.SetActive(true);
                view.Background.color = target.Color;
                view.Label.text = ComposeLabel(target);
                view.Root.anchoredPosition = WorldToIndicatorPosition(worldCamera, target.Target.position);
            }

            foreach (var pair in _views)
            {
                if (!_visibleViews.Contains(pair.Key) && pair.Value.Root != null)
                {
                    pair.Value.Root.gameObject.SetActive(false);
                }
            }
        }

        private string ComposeLabel(IndicatorTarget target)
        {
            float distance = 0f;
            if (_localPlayer != null && _localPlayer.player != null && target.Target != null)
            {
                distance = Vector3.Distance(_localPlayer.player.transform.position, target.Target.position);
            }

            return $"{target.Label} {Mathf.RoundToInt(distance)}m";
        }

        private Vector2 WorldToIndicatorPosition(Camera worldCamera, Vector3 worldPosition)
        {
            Vector3 screenPosition = worldCamera.WorldToScreenPoint(worldPosition);
            Vector2 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f);

            if (screenPosition.z < 0f)
            {
                Vector3 toTarget = worldPosition - worldCamera.transform.position;
                Vector2 cameraPlaneDirection = new(
                    Vector3.Dot(worldCamera.transform.right, toTarget),
                    Vector3.Dot(worldCamera.transform.up, toTarget));

                if (cameraPlaneDirection.sqrMagnitude < 0.0001f)
                {
                    cameraPlaneDirection = Vector2.down;
                }

                Vector2 edgePosition = screenCenter +
                    cameraPlaneDirection.normalized * Mathf.Max(Screen.width, Screen.height);
                screenPosition = new Vector3(edgePosition.x, edgePosition.y, 0f);
            }

            float padding = Mathf.Max(0f, edgePadding);
            Vector2 clampedScreenPosition = new(
                Mathf.Clamp(screenPosition.x, padding, Screen.width - padding),
                Mathf.Clamp(screenPosition.y, padding, Screen.height - padding));

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                indicatorRoot,
                clampedScreenPosition,
                _uiCamera,
                out Vector2 localPosition);
            return localPosition;
        }

        private IndicatorView GetOrCreateView(int id)
        {
            if (_views.TryGetValue(id, out var existing) && existing.Root != null)
            {
                return existing;
            }

            var rootObject = new GameObject($"WorldIndicator_{id}", typeof(RectTransform), typeof(Image));
            rootObject.layer = indicatorRoot.gameObject.layer;
            var rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.SetParent(indicatorRoot, false);
            rootRect.sizeDelta = new Vector2(markerWidth, markerHeight);
            rootRect.pivot = new Vector2(0.5f, 0.5f);

            var background = rootObject.GetComponent<Image>();
            background.raycastTarget = false;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.layer = rootObject.layer;
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(rootRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 2f);
            textRect.offsetMax = new Vector2(-6f, -2f);

            var label = textObject.GetComponent<TextMeshProUGUI>();
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.white;
            label.fontSize = 16f;
            label.raycastTarget = false;

            var view = new IndicatorView
            {
                Root = rootRect,
                Background = background,
                Label = label,
            };
            _views[id] = view;
            return view;
        }

        private void HideAll()
        {
            foreach (var pair in _views)
            {
                if (pair.Value.Root != null)
                {
                    pair.Value.Root.gameObject.SetActive(false);
                }
            }
        }

        private void EnsureRoot()
        {
            if (indicatorRoot != null)
            {
                return;
            }

            _canvas = GetComponentInParent<Canvas>();
            _uiCamera = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            var rootObject = new GameObject("WorldDirectionIndicators", typeof(RectTransform));
            rootObject.layer = gameObject.layer;
            indicatorRoot = rootObject.GetComponent<RectTransform>();
            indicatorRoot.SetParent(transform, false);
            indicatorRoot.anchorMin = Vector2.zero;
            indicatorRoot.anchorMax = Vector2.one;
            indicatorRoot.offsetMin = Vector2.zero;
            indicatorRoot.offsetMax = Vector2.zero;
            indicatorRoot.SetAsLastSibling();
        }

        private void ApplyVisibility()
        {
            EnsureRoot();
            if (indicatorRoot != null)
            {
                indicatorRoot.gameObject.SetActive(_hudVisible && _role == PlayerRole.Survivor);
            }
        }

        private struct IndicatorTarget
        {
            public int Id;
            public Transform Target;
            public string Label;
            public Color Color;
        }

        private struct IndicatorView
        {
            public RectTransform Root;
            public Image Background;
            public TextMeshProUGUI Label;
        }
    }
}
