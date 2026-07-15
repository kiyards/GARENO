using UnityEditor;
using UnityEngine;

namespace MultiplayerFork.LevelDesign.Editor
{
    internal sealed class UnifiedLevelDesignEditorSession : ScriptableSingleton<UnifiedLevelDesignEditorSession>
    {
        public LevelLayoutData activeLayoutData;
        public GeneratedLevelMarker activeGeneratedRoot;
        public ProceduralLayoutProfile defaultProceduralProfile;
        public ImageTraceProfile defaultImageTraceProfile;
        public WallBuildProfile defaultWallBuildProfile;
        public ModularPieceLibrary defaultModularPieceLibrary;
        public DecorationRuleSet defaultDecorationRuleSet;
        public bool drawWallPaths = true;
        public bool drawDoorMarkers = true;
        public bool drawPathLabels = false;
        public bool drawBounds = true;
        public Color wallPathColor = new Color(0.95f, 0.72f, 0.52f, 1f);
        public Color doorColor = new Color(0.4f, 0.9f, 1f, 1f);
        public Color boundsColor = new Color(0.3f, 1f, 0.75f, 1f);
        public int activeTab;
        public GameObject quickWallPrefab;
        public GameObject quickDoorPrefab;
        public float quickWallPieceSpacing = 2f;
        public float quickWallHeight = 4f;
        public float quickWallThickness = 0.2f;
        public float quickCorridorWidth = 2f;
        public int quickRoomCount = 7;
        public bool quickGenerateCorridors = true;
        public Vector2 quickLayoutSize = new Vector2(44f, 34f);
        public Vector2 quickMinRoomSize = new Vector2(6f, 5f);
        public Vector2 quickMaxRoomSize = new Vector2(10f, 8f);
        public float quickRoomPadding = 2f;
        public int quickSeed = 20260715;
        public bool quickRandomizeSeedOnGenerate = true;

        public void Save()
        {
            Save(true);
        }
    }
}
