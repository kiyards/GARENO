using UnityEngine;

namespace MultiplayerFork.LevelDesign
{
    [CreateAssetMenu(fileName = "DecorationRuleSet", menuName = "Level Design/Decoration Rule Set")]
    public sealed class DecorationRuleSet : ScriptableObject
    {
        [TextArea] public string notes = "Phase 1 placeholder for later decoration rules.";
    }
}
