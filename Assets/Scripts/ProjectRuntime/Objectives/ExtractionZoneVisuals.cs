using System.Collections.Generic;
using ProjectRuntime.Managers;
using UnityEngine;
using UnityEngine.Rendering;

namespace ProjectRuntime.Objectives
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public class ExtractionZoneVisuals : MonoBehaviour
    {
        [ColorUsage(true, true)]
        [SerializeField] private Color unavailableWallColor = new(2f, 0f, 0f, 1f);

        [ColorUsage(true, true)]
        [SerializeField] private Color availableWallColor = new(0f, 2f, 0f, 1f);

        [SerializeField] private float wallHeight = 1f;

        [Range(0f, 1f)]
        [SerializeField] private float bottomAlpha = 0.6f;

        [Range(0f, 1f)]
        [SerializeField] private float topAlpha = 0f;

        [Min(1f)]
        [SerializeField] private float fadePower = 4f;

        [Range(1, 32)]
        [SerializeField] private int verticalSegments = 10;

        private BattleManager _boundBattleManager;
        private Material _wallMaterial;
        private Mesh _wallMesh;
        private bool _hasAppliedReadyState;
        private bool _lastReadyState;

        private void Awake()
        {
            var box = GetComponent<BoxCollider>();
            Vector3 center = box.center;
            Vector3 localSize = box.size;

            float xMin = center.x - localSize.x * 0.5f;
            float xMax = center.x + localSize.x * 0.5f;
            float zMin = center.z - localSize.z * 0.5f;
            float zMax = center.z + localSize.z * 0.5f;
            float yBottom = center.y - localSize.y * 0.5f;
            float yTop = yBottom + this.wallHeight;

            var wallsObj = new GameObject("ExtractionZoneWalls");
            wallsObj.transform.SetParent(transform, false);

            var meshFilter = wallsObj.AddComponent<MeshFilter>();
            this._wallMesh = BuildWalls(xMin, xMax, zMin, zMax, yBottom, yTop);
            meshFilter.sharedMesh = this._wallMesh;

            var meshRenderer = wallsObj.AddComponent<MeshRenderer>();
            this._wallMaterial = CreateWallMaterial();
            meshRenderer.sharedMaterial = this._wallMaterial;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        private void OnEnable()
        {
            TryBindBattleManager();
            RefreshWallColor();
        }

        private void OnDisable()
        {
            UnbindBattleManager();
        }

        private void Update()
        {
            if (this._boundBattleManager == null)
            {
                TryBindBattleManager();
            }
        }

        private void OnDestroy()
        {
            if (this._wallMaterial != null)
            {
                Destroy(this._wallMaterial);
            }

            if (this._wallMesh != null)
            {
                Destroy(this._wallMesh);
            }
        }

        private void TryBindBattleManager()
        {
            if (this._boundBattleManager == BattleManager.Instance)
            {
                return;
            }

            UnbindBattleManager();

            if (BattleManager.Instance == null)
            {
                RefreshWallColor();
                return;
            }

            this._boundBattleManager = BattleManager.Instance;
            this._boundBattleManager.OnRoundStateChanged += RefreshWallColor;
            RefreshWallColor();
        }

        private void UnbindBattleManager()
        {
            if (this._boundBattleManager == null)
            {
                return;
            }

            this._boundBattleManager.OnRoundStateChanged -= RefreshWallColor;
            this._boundBattleManager = null;
        }

        private void RefreshWallColor()
        {
            if (this._wallMaterial == null)
            {
                return;
            }

            bool extractionAvailable = BattleManager.Instance != null &&
                                       (BattleManager.Instance.CurrentRoundPhase == RoundPhase.CrystalsComplete ||
                                        BattleManager.Instance.Winner == RoundWinner.Survivors);
            if (this._hasAppliedReadyState && extractionAvailable == this._lastReadyState)
            {
                return;
            }

            this._hasAppliedReadyState = true;
            this._lastReadyState = extractionAvailable;
            ApplyMaterialColor(
                this._wallMaterial,
                extractionAvailable ? this.availableWallColor : this.unavailableWallColor);
        }

        private Mesh BuildWalls(float xMin, float xMax, float zMin, float zMax, float yBottom, float yTop)
        {
            var corners = new[]
            {
                new Vector2(xMin, zMin),
                new Vector2(xMax, zMin),
                new Vector2(xMax, zMax),
                new Vector2(xMin, zMax),
            };

            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            int rows = Mathf.Max(1, this.verticalSegments);

            for (int edge = 0; edge < corners.Length; edge++)
            {
                Vector2 a = corners[edge];
                Vector2 b = corners[(edge + 1) % corners.Length];
                int baseIndex = vertices.Count;

                for (int row = 0; row <= rows; row++)
                {
                    float t = (float)row / rows;
                    float y = Mathf.Lerp(yBottom, yTop, t);
                    float alpha = Mathf.Lerp(this.topAlpha, this.bottomAlpha, Mathf.Pow(1f - t, this.fadePower));
                    var color = new Color(1f, 1f, 1f, alpha);

                    vertices.Add(new Vector3(a.x, y, a.y));
                    vertices.Add(new Vector3(b.x, y, b.y));
                    colors.Add(color);
                    colors.Add(color);
                    uvs.Add(new Vector2(0f, t));
                    uvs.Add(new Vector2(1f, t));
                }

                for (int row = 0; row < rows; row++)
                {
                    int row0 = baseIndex + row * 2;
                    int row1 = row0 + 2;
                    triangles.Add(row0);
                    triangles.Add(row0 + 1);
                    triangles.Add(row1 + 1);
                    triangles.Add(row0);
                    triangles.Add(row1 + 1);
                    triangles.Add(row1);
                }
            }

            var mesh = new Mesh { name = "ExtractionZoneWalls" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Material CreateWallMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            }

            var material = new Material(shader);
            ApplyMaterialColor(material, this.unavailableWallColor);
            return material;
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }
        }
    }
}
