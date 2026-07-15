using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [Serializable]
    public sealed class RoomData
    {
        public string roomId = Guid.NewGuid().ToString("N");
        public string displayName = "Room";
        public Rect bounds = new Rect(0f, 0f, 10f, 10f);
        public bool isLandmark;
    }

    [Serializable]
    public sealed class CorridorData
    {
        public string corridorId = Guid.NewGuid().ToString("N");
        public string displayName = "Corridor";
        public List<Vector3> localPoints = new List<Vector3>();
        public float width = 2f;
    }

    [Serializable]
    public sealed class LayoutZoneData
    {
        public string zoneId = Guid.NewGuid().ToString("N");
        public string displayName = "Zone";
        public LayoutZoneType zoneType = LayoutZoneType.Room;
        public List<Vector3> localPolygon = new List<Vector3>();
    }
}
