using System;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [Serializable]
    public sealed class LayoutSourceMetadata
    {
        public LayoutSourceMode sourceMode = LayoutSourceMode.Procedural;
        public string layoutId = Guid.NewGuid().ToString("N");
        public string sourceImageAssetPath = string.Empty;
        public string sourceImageGuid = string.Empty;
        public string calibrationProfileId = string.Empty;
        public int generationSeed;
        public Vector2 levelSize = new Vector2(50f, 50f);
        public float cellSize = 2f;
        public Vector3 worldOrigin = Vector3.zero;
        public Vector3 worldEulerRotation = Vector3.zero;
        public bool horizontalFlip;
        public bool verticalFlip;
        [Range(0f, 1f)] public float imageOpacity = 0.65f;
        public float imageElevation = 0.05f;
        public string notes = string.Empty;
    }
}
