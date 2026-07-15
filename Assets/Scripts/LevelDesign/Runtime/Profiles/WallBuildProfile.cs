using System;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [CreateAssetMenu(fileName = "WallBuildProfile", menuName = "Level Design/Wall Build Profile")]
    public sealed class WallBuildProfile : ScriptableObject
    {
        public string adapterId = "UnitySplineInstantiateAdapter";
        public ModularPieceLibrary modularPieceLibrary;
        public float wallHeight = 4f;
        public float spacing = 2f;
        public FitMode fitMode = FitMode.TrimLastSegment;
        public bool createColliders = true;
        public Vector3 localOffset = Vector3.zero;
        public Vector3 localEulerOffset = Vector3.zero;

        public enum FitMode
        {
            StretchLastSegment,
            TrimLastSegment,
            ExactSpacing
        }
    }
}
