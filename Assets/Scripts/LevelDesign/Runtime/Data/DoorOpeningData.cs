using System;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [Serializable]
    public sealed class DoorOpeningData
    {
        public string openingId = Guid.NewGuid().ToString("N");
        public string wallPathId = string.Empty;
        public string displayName = "Door";
        public float distanceAlongPath;
        public float width = 1.5f;
        public float height = 2.2f;
        public string doorType = "Standard";
        public GameObject doorPrefab;
        public Vector3 localOffset = Vector3.zero;
        public Vector3 localEulerOffset = Vector3.zero;
    }
}
