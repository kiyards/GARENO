using System;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [CreateAssetMenu(fileName = "ImageTraceProfile", menuName = "Level Design/Image Trace Profile")]
    public sealed class ImageTraceProfile : ScriptableObject
    {
        public ThresholdSettings threshold = new ThresholdSettings();
        public CleanupSettings cleanup = new CleanupSettings();
        public SimplificationSettings simplification = new SimplificationSettings();

        [Serializable]
        public sealed class ThresholdSettings
        {
            [Range(0f, 1f)] public float grayscaleThreshold = 0.5f;
            [Range(0f, 1f)] public float edgeSensitivity = 0.5f;
            public bool invertMask;
        }

        [Serializable]
        public sealed class CleanupSettings
        {
            public float minimumSegmentLength = 0.5f;
            public float gapClosingDistance = 0.35f;
            public int noiseRemovalPixels = 8;
        }

        [Serializable]
        public sealed class SimplificationSettings
        {
            public float pathTolerance = 0.1f;
            public float cornerTolerance = 15f;
        }
    }
}
