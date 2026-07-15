namespace MultiplayerFork.LevelDesign
{
    public enum LayoutSourceMode
    {
        Procedural,
        ImageImport,
        Existing
    }

    public enum WallPathUsage
    {
        Wall,
        Doorway,
        Perimeter,
        Ignored
    }

    public enum LayoutZoneType
    {
        Room,
        Corridor,
        OpenArea,
        Floor,
        Ceiling,
        Landmark,
        Reserved
    }
}
