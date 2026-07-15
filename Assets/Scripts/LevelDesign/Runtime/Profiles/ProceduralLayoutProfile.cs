using System;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [CreateAssetMenu(fileName = "ProceduralLayoutProfile", menuName = "Level Design/Procedural Layout Profile")]
    public sealed class ProceduralLayoutProfile : ScriptableObject
    {
        public AreaSettings area = new AreaSettings();
        public RoomSettings rooms = new RoomSettings();
        public CorridorSettings corridors = new CorridorSettings();
        public ConnectivitySettings connectivity = new ConnectivitySettings();

        [Serializable]
        public sealed class AreaSettings
        {
            public Vector2 levelSize = new Vector2(50f, 50f);
            public float cellSize = 2f;
            public int defaultSeed = 12345;
            public bool randomizeSeedOnCreate;
        }

        [Serializable]
        public sealed class RoomSettings
        {
            public Vector2 minRoomSize = new Vector2(4f, 4f);
            public Vector2 maxRoomSize = new Vector2(12f, 12f);
            public int targetRoomCount = 8;
            [Range(0f, 1f)] public float roomDensity = 0.45f;
        }

        [Serializable]
        public sealed class CorridorSettings
        {
            public float corridorWidth = 2f;
            [Range(0f, 1f)] public float branchingAmount = 0.35f;
            [Range(0f, 1f)] public float loopFrequency = 0.2f;
        }

        [Serializable]
        public sealed class ConnectivitySettings
        {
            public int entranceCount = 1;
            public int exitCount = 1;
            public int maxGenerationAttempts = 20;
        }
    }
}
