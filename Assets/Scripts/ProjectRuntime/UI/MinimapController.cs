using System.Collections.Generic;
using ProjectRuntime.Actor;
using ProjectRuntime.Actor.PlayerStates;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using ProjectRuntime.Objectives;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectRuntime.UI
{
    public class MinimapController : MonoBehaviour
    {
        private const string MinimapCameraName = "MinimapCamera";

        [Header("UI")]
        [SerializeField] private RectTransform minimapRoot;
        [SerializeField] private RawImage mapImage;
        [SerializeField] private RectTransform blipRoot;
        [SerializeField] private Vector2 minimapSize = new(220f, 220f);
        [SerializeField] private Vector2 minimapOffset = new(24f, -24f);

        [Header("Camera")]
        [SerializeField] private Camera minimapCamera;
        [SerializeField] private Camera minimapCameraPrefab;
        [SerializeField] private int renderTextureSize = 512;
        [SerializeField] private float cameraHeight = 120f;
        [SerializeField] private float boundsPadding = 1.08f;
        [SerializeField] private Vector3 fallbackBoundsCenter;
        [SerializeField] private Vector3 fallbackBoundsSize = new(140f, 40f, 140f);

        [Header("Blips")]
        [SerializeField] private float defaultBlipSize = 8f;
        [SerializeField] private float localPlayerBlipSize = 13f;
        [SerializeField] private float objectiveBlipSize = 10f;
        [SerializeField] private float nemesisBlipSize = 15f;
        [SerializeField] private Color localPlayerColor = new(0.35f, 1f, 0.45f, 1f);
        [SerializeField] private Color survivorColor = new(0.35f, 0.8f, 1f, 1f);
        [SerializeField] private Color dungeonMasterColor = new(1f, 0.2f, 0.22f, 1f);
        [SerializeField] private Color crystalColor = new(0.7f, 0.35f, 1f, 1f);
        [SerializeField] private Color enemyColor = new(1f, 0.45f, 0.05f, 1f);
        [SerializeField] private Color trapColor = new(1f, 0.95f, 0.05f, 1f);
        [SerializeField] private Color turretColor = new(1f, 0.55f, 0.1f, 1f);

        [Header("Refresh")]
        [SerializeField] private float targetRefreshInterval = 0.5f;

        private readonly List<MinimapTarget> _targets = new();
        private readonly Dictionary<int, RawImage> _blips = new();
        private readonly HashSet<int> _visibleBlips = new();
        private RenderTexture _renderTexture;
        private Texture2D _blipTexture;
        private PlayerManager _localPlayer;
        private PlayerRole _role = PlayerRole.Unassigned;
        private BattleManager _battleManager;
        private Bounds _worldBounds;
        private float _nextTargetRefreshTime;
        private bool _hudVisible = true;

        private void Awake()
        {
            EnsureUi();
            EnsureCamera();
            RefreshWorldBounds();
            RefreshTargets();
            ApplyVisibility();
        }

        private void OnDestroy()
        {
            if (_renderTexture != null)
            {
                if (minimapCamera != null && minimapCamera.targetTexture == _renderTexture)
                {
                    minimapCamera.targetTexture = null;
                }

                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_blipTexture != null)
            {
                Destroy(_blipTexture);
                _blipTexture = null;
            }
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextTargetRefreshTime)
            {
                _nextTargetRefreshTime = Time.unscaledTime + Mathf.Max(0.1f, targetRefreshInterval);
                RefreshWorldBounds();
                RefreshTargets();
            }

            UpdateCameraFrame();
            UpdateBlips();
        }

        public void BindLocalPlayer(PlayerManager player)
        {
            _localPlayer = player;
            ApplyVisibility();
            RefreshTargets();
        }

        public void SetRole(PlayerRole role)
        {
            _role = role;
            ApplyVisibility();
            RefreshTargets();
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

        private void EnsureUi()
        {
            if (minimapRoot == null)
            {
                var rootObject = new GameObject("MinimapRoot", typeof(RectTransform), typeof(Image));
                rootObject.layer = gameObject.layer;
                minimapRoot = rootObject.GetComponent<RectTransform>();
                minimapRoot.SetParent(transform, false);
                minimapRoot.anchorMin = new Vector2(0f, 1f);
                minimapRoot.anchorMax = new Vector2(0f, 1f);
                minimapRoot.pivot = new Vector2(0f, 1f);
                minimapRoot.anchoredPosition = minimapOffset;
                minimapRoot.sizeDelta = minimapSize;

                var background = rootObject.GetComponent<Image>();
                background.color = new Color(0f, 0f, 0f, 0.72f);
                background.raycastTarget = false;
            }

            if (mapImage == null)
            {
                var mapObject = new GameObject("MinimapMapImage", typeof(RectTransform), typeof(RawImage));
                mapObject.layer = minimapRoot.gameObject.layer;
                var mapRect = mapObject.GetComponent<RectTransform>();
                mapRect.SetParent(minimapRoot, false);
                mapRect.anchorMin = Vector2.zero;
                mapRect.anchorMax = Vector2.one;
                mapRect.offsetMin = new Vector2(4f, 4f);
                mapRect.offsetMax = new Vector2(-4f, -4f);

                mapImage = mapObject.GetComponent<RawImage>();
                mapImage.color = Color.white;
                mapImage.raycastTarget = false;
            }

            if (blipRoot == null)
            {
                var blipObject = new GameObject("MinimapBlips", typeof(RectTransform));
                blipObject.layer = minimapRoot.gameObject.layer;
                blipRoot = blipObject.GetComponent<RectTransform>();
                blipRoot.SetParent(minimapRoot, false);
                blipRoot.anchorMin = Vector2.zero;
                blipRoot.anchorMax = Vector2.one;
                blipRoot.offsetMin = Vector2.zero;
                blipRoot.offsetMax = Vector2.zero;
            }
        }

        private void EnsureCamera()
        {
            if (minimapCamera == null)
            {
                var cameraObject = GameObject.Find(MinimapCameraName);
                if (cameraObject != null)
                {
                    minimapCamera = cameraObject.GetComponent<Camera>();
                }
            }

            if (minimapCamera == null)
            {
                minimapCamera = minimapCameraPrefab != null
                    ? Instantiate(minimapCameraPrefab)
                    : new GameObject(MinimapCameraName).AddComponent<Camera>();
                minimapCamera.name = MinimapCameraName;
            }

            minimapCamera.orthographic = true;
            minimapCamera.clearFlags = CameraClearFlags.SolidColor;
            minimapCamera.backgroundColor = new Color(0.045f, 0.05f, 0.055f, 1f);
            minimapCamera.depth = -20f;
            minimapCamera.cullingMask = BuildMapCullingMask();

            if (_renderTexture == null)
            {
                int size = Mathf.Max(128, renderTextureSize);
                _renderTexture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
                {
                    name = "MinimapRenderTexture"
                };
                _renderTexture.Create();
            }

            minimapCamera.targetTexture = _renderTexture;
            if (mapImage != null)
            {
                mapImage.texture = _renderTexture;
            }
        }

        private int BuildMapCullingMask()
        {
            int mask = 0;
            AddLayerToMask("Default", ref mask);
            AddLayerToMask("TransparentFX", ref mask);
            AddLayerToMask("Ground", ref mask);
            AddLayerToMask("Water", ref mask);
            AddLayerToMask("Wall", ref mask);
            AddLayerToMask("Door", ref mask);
            return mask == 0 ? ~LayerMask.GetMask("UI") : mask;
        }

        private static void AddLayerToMask(string layerName, ref int mask)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer >= 0)
            {
                mask |= 1 << layer;
            }
        }

        private void RefreshWorldBounds()
        {
            var boundsCollider = DungeonMasterCameraBounds.FindBoundingVolume();
            if (boundsCollider != null)
            {
                _worldBounds = boundsCollider.bounds;
                return;
            }

            bool hasBounds = false;
            Bounds rendererBounds = default;
            foreach (var targetRenderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!IsMapBoundsRenderer(targetRenderer))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    rendererBounds = targetRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    rendererBounds.Encapsulate(targetRenderer.bounds);
                }
            }

            _worldBounds = hasBounds
                ? rendererBounds
                : new Bounds(fallbackBoundsCenter, fallbackBoundsSize);
        }

        private static bool IsMapBoundsRenderer(Renderer targetRenderer)
        {
            if (targetRenderer == null || targetRenderer.gameObject.layer == LayerMask.NameToLayer("UI"))
            {
                return false;
            }

            Transform targetTransform = targetRenderer.transform;
            return targetTransform.GetComponentInParent<GameplayPlayer>() == null &&
                   targetTransform.GetComponentInParent<ZombieEnemy>() == null &&
                   targetTransform.GetComponentInParent<BearTrap>() == null &&
                   targetTransform.GetComponentInParent<DungeonMasterTurret>() == null;
        }

        private void UpdateCameraFrame()
        {
            if (minimapCamera == null)
            {
                return;
            }

            Vector3 center = _worldBounds.center;
            float y = Mathf.Max(_worldBounds.max.y + cameraHeight, cameraHeight);
            minimapCamera.transform.SetPositionAndRotation(
                new Vector3(center.x, y, center.z),
                Quaternion.Euler(90f, 0f, 0f));

            float maxSize = Mathf.Max(_worldBounds.size.x, _worldBounds.size.z, 1f);
            minimapCamera.orthographicSize = maxSize * 0.5f * Mathf.Max(1f, boundsPadding);
            minimapCamera.nearClipPlane = 0.1f;
            minimapCamera.farClipPlane = Mathf.Max(cameraHeight * 2f, _worldBounds.size.y + cameraHeight * 2f);
        }

        private void RefreshTargets()
        {
            _targets.Clear();

            var battleManager = _battleManager != null ? _battleManager : BattleManager.Instance;
            if (_localPlayer == null || battleManager == null)
            {
                return;
            }

            AddPlayerTargets(battleManager);
            AddObjectiveTargets(battleManager);

            if (_role == PlayerRole.DungeonMaster)
            {
                AddDungeonMasterTargets();
            }
        }

        private void AddPlayerTargets(BattleManager battleManager)
        {
            foreach (var player in battleManager.Players)
            {
                if (player == null)
                {
                    continue;
                }

                bool isLocal = player == _localPlayer;
                bool isSurvivor = player.playerRole == PlayerRole.Survivor;
                bool isDungeonMaster = player.playerRole == PlayerRole.DungeonMaster;

                if (isLocal && _role == PlayerRole.DungeonMaster &&
                    player.player != null &&
                    player.player.currentState is DungeonMasterNemesisState)
                {
                    continue;
                }

                if (_role == PlayerRole.Survivor && !isSurvivor)
                {
                    continue;
                }

                if (_role == PlayerRole.DungeonMaster && !isSurvivor && !isDungeonMaster)
                {
                    continue;
                }

                Transform target = player.player != null ? player.player.transform : player.transform;
                Color color = isLocal ? localPlayerColor :
                    isDungeonMaster ? dungeonMasterColor : survivorColor;
                float size = isLocal ? localPlayerBlipSize : defaultBlipSize;
                AddTarget(target, color, size, isLocal, 10);
            }
        }

        private void AddObjectiveTargets(BattleManager battleManager)
        {
            if (_role == PlayerRole.DungeonMaster)
            {
                foreach (var crystal in FindObjectsByType<CrystalObjective>(FindObjectsSortMode.None))
                {
                    if (crystal != null && !crystal.IsDespawned)
                    {
                        AddTarget(crystal.transform, crystalColor, objectiveBlipSize, false, 20);
                    }
                }
            }

        }

        private void AddDungeonMasterTargets()
        {
            foreach (var enemy in FindObjectsByType<ZombieEnemy>(FindObjectsSortMode.None))
            {
                if (enemy != null)
                {
                    AddTarget(enemy.transform, enemyColor, defaultBlipSize, false, 40);
                }
            }

            foreach (var nemesis in FindObjectsByType<DungeonMasterNemesis>(FindObjectsSortMode.None))
            {
                if (nemesis != null)
                {
                    AddTarget(nemesis.transform, enemyColor, nemesisBlipSize, false, 45);
                }
            }

            AddTrapTargets<BearTrap>();
            AddTrapTargets<C4Trap>();
            AddTrapTargets<FlashbangTrap>();

            foreach (var turret in FindObjectsByType<DungeonMasterTurret>(FindObjectsSortMode.None))
            {
                if (turret != null)
                {
                    AddTarget(turret.transform, turretColor, defaultBlipSize, false, 60);
                }
            }
        }

        private void AddTrapTargets<T>() where T : Component, ITrap
        {
            foreach (var trap in FindObjectsByType<T>(FindObjectsSortMode.None))
            {
                if (trap != null)
                {
                    AddTarget(trap.transform, trapColor, defaultBlipSize, false, 50);
                }
            }
        }

        private void AddTarget(Transform target, Color color, float size, bool rotateWithTarget, int category)
        {
            if (target == null)
            {
                return;
            }

            int id = target.GetInstanceID() ^ category;
            _targets.Add(new MinimapTarget
            {
                Id = id,
                Target = target,
                Color = color,
                Size = size,
                RotateWithTarget = rotateWithTarget
            });
        }

        private void UpdateBlips()
        {
            if (blipRoot == null)
            {
                return;
            }

            _visibleBlips.Clear();
            foreach (var target in _targets)
            {
                if (target.Target == null)
                {
                    continue;
                }

                var blip = GetOrCreateBlip(target.Id);
                _visibleBlips.Add(target.Id);
                blip.gameObject.SetActive(true);
                blip.color = target.Color;

                var rect = blip.rectTransform;
                rect.sizeDelta = new Vector2(target.Size, target.Size);
                rect.anchoredPosition = WorldToMinimapPosition(target.Target.position);
                rect.localEulerAngles = target.RotateWithTarget
                    ? new Vector3(0f, 0f, -target.Target.eulerAngles.y)
                    : Vector3.zero;
            }

            foreach (var pair in _blips)
            {
                if (!_visibleBlips.Contains(pair.Key) && pair.Value != null)
                {
                    pair.Value.gameObject.SetActive(false);
                }
            }
        }

        private RawImage GetOrCreateBlip(int id)
        {
            if (_blips.TryGetValue(id, out var existing) && existing != null)
            {
                return existing;
            }

            var blipObject = new GameObject($"MinimapBlip_{id}", typeof(RectTransform), typeof(RawImage));
            blipObject.layer = blipRoot.gameObject.layer;
            var rect = blipObject.GetComponent<RectTransform>();
            rect.SetParent(blipRoot, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            var blip = blipObject.GetComponent<RawImage>();
            blip.texture = GetBlipTexture();
            blip.raycastTarget = false;
            _blips[id] = blip;
            return blip;
        }

        private Texture2D GetBlipTexture()
        {
            if (_blipTexture != null)
            {
                return _blipTexture;
            }

            _blipTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "MinimapBlipTexture"
            };
            _blipTexture.SetPixel(0, 0, Color.white);
            _blipTexture.Apply();
            return _blipTexture;
        }

        private Vector2 WorldToMinimapPosition(Vector3 worldPosition)
        {
            Rect rect = blipRoot.rect;
            Vector3 center = minimapCamera != null
                ? minimapCamera.transform.position
                : _worldBounds.center;
            float halfHeight = minimapCamera != null
                ? Mathf.Max(0.001f, minimapCamera.orthographicSize)
                : Mathf.Max(0.001f, _worldBounds.size.z * 0.5f);
            float halfWidth = minimapCamera != null
                ? halfHeight * Mathf.Max(0.001f, minimapCamera.aspect)
                : Mathf.Max(0.001f, _worldBounds.size.x * 0.5f);

            float normalizedX = Mathf.Clamp((worldPosition.x - center.x) / halfWidth, -1f, 1f);
            float normalizedY = Mathf.Clamp((worldPosition.z - center.z) / halfHeight, -1f, 1f);

            return new Vector2(
                normalizedX * rect.width * 0.5f,
                normalizedY * rect.height * 0.5f);
        }

        private void ApplyVisibility()
        {
            if (minimapRoot == null)
            {
                return;
            }

            bool shouldShow = _hudVisible &&
                              _localPlayer != null &&
                              (_role == PlayerRole.Survivor || _role == PlayerRole.DungeonMaster);
            minimapRoot.gameObject.SetActive(shouldShow);

            if (minimapCamera != null)
            {
                minimapCamera.enabled = shouldShow;
            }
        }

        private struct MinimapTarget
        {
            public int Id;
            public Transform Target;
            public Color Color;
            public float Size;
            public bool RotateWithTarget;
        }
    }
}
