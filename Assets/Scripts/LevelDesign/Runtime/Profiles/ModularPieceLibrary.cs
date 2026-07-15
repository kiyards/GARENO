using System;
using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [CreateAssetMenu(fileName = "ModularPieceLibrary", menuName = "Level Design/Modular Piece Library")]
    public sealed class ModularPieceLibrary : ScriptableObject
    {
        public List<PieceEntry> straightWalls = new List<PieceEntry>();
        public List<PieceEntry> corners = new List<PieceEntry>();
        public List<PieceEntry> doorFrames = new List<PieceEntry>();
        public List<PieceEntry> floors = new List<PieceEntry>();
        public List<PieceEntry> ceilings = new List<PieceEntry>();

        [Serializable]
        public sealed class PieceEntry
        {
            public string displayName = "Piece";
            public GameObject prefab;
            [Range(0f, 1f)] public float spawnWeight = 1f;
            public float nominalLength = 2f;
            public Vector3 localOffset = Vector3.zero;
            public Vector3 localEulerOffset = Vector3.zero;
        }
    }
}
