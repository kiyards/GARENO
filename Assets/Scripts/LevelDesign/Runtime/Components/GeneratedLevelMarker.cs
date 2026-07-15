using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    public sealed class GeneratedLevelMarker : MonoBehaviour
    {
        [SerializeField] private string generatedId = System.Guid.NewGuid().ToString("N");
        [SerializeField] private string layoutId = string.Empty;
        [SerializeField] private LayoutSourceMode sourceMode;
        [SerializeField] private int generationSeed;
        [SerializeField] private GeneratedLayoutState bakeState = GeneratedLayoutState.Preview;
        [SerializeField] private string toolVersion = "Phase1";
        [SerializeField] private LevelLayoutData layoutData;

        public string GeneratedId => generatedId;
        public string LayoutId => layoutId;
        public LayoutSourceMode SourceMode => sourceMode;
        public int GenerationSeed => generationSeed;
        public GeneratedLayoutState BakeState => bakeState;
        public LevelLayoutData LayoutData => layoutData;

        public void SyncFrom(LevelLayoutData source)
        {
            layoutData = source;
            if (source == null)
            {
                layoutId = string.Empty;
                sourceMode = LayoutSourceMode.Existing;
                generationSeed = 0;
                bakeState = GeneratedLayoutState.Preview;
                return;
            }

            source.ResetIdsIfMissing();
            layoutId = source.SourceMetadata.layoutId;
            sourceMode = source.SourceMetadata.sourceMode;
            generationSeed = source.SourceMetadata.generationSeed;
            bakeState = source.State;
        }
    }
}
