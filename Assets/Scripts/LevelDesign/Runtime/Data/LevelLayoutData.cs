using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [CreateAssetMenu(fileName = "LevelLayoutData", menuName = "Level Design/Level Layout Data")]
    public sealed class LevelLayoutData : ScriptableObject
    {
        [SerializeField] private string layoutName = "New Layout";
        [SerializeField] private GeneratedLayoutState state = GeneratedLayoutState.Preview;
        [SerializeField] private LayoutSourceMetadata sourceMetadata = new LayoutSourceMetadata();
        [SerializeField] private List<RoomData> rooms = new List<RoomData>();
        [SerializeField] private List<CorridorData> corridors = new List<CorridorData>();
        [SerializeField] private List<WallPathData> wallPaths = new List<WallPathData>();
        [SerializeField] private List<DoorOpeningData> doorOpenings = new List<DoorOpeningData>();
        [SerializeField] private List<LayoutZoneData> floorZones = new List<LayoutZoneData>();
        [SerializeField] private List<LayoutZoneData> ceilingZones = new List<LayoutZoneData>();

        public string LayoutName
        {
            get => layoutName;
            set => layoutName = value;
        }

        public GeneratedLayoutState State
        {
            get => state;
            set => state = value;
        }

        public LayoutSourceMetadata SourceMetadata => sourceMetadata;
        public List<RoomData> Rooms => rooms;
        public List<CorridorData> Corridors => corridors;
        public List<WallPathData> WallPaths => wallPaths;
        public List<DoorOpeningData> DoorOpenings => doorOpenings;
        public List<LayoutZoneData> FloorZones => floorZones;
        public List<LayoutZoneData> CeilingZones => ceilingZones;

        public void ResetIdsIfMissing()
        {
            if (string.IsNullOrWhiteSpace(sourceMetadata.layoutId))
            {
                sourceMetadata.layoutId = System.Guid.NewGuid().ToString("N");
            }
        }
    }
}
