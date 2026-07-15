using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [Serializable]
    public sealed class WallPathData
    {
        public string pathId = Guid.NewGuid().ToString("N");
        public string displayName = "WallPath";
        public WallPathUsage usage = WallPathUsage.Wall;
        public bool isClosed;
        public bool isSplineBaked;
        public string wallProfileId = string.Empty;
        public string sourceTraceId = string.Empty;
        public string sourceImageGuid = string.Empty;
        public Color debugColor = new Color(0.92f, 0.72f, 0.52f, 1f);
        public List<Vector3> localPoints = new List<Vector3>();
    }
}
