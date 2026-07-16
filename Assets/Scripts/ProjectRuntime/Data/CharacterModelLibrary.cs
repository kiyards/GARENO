using System;
using System.Collections.Generic;
using ProjectRuntime.Network;
using UnityEngine;

/// <summary>
/// Maps a survivor's assigned ability to the character model that should represent them
/// (model prefab, ghost/corpse variants, animator controller, and the animator state
/// names <see cref="ProjectRuntime.Actor.PlayerVisualAnimator"/> plays). Wired in the
/// inspector so art can assign per-character assets without code changes.
/// </summary>
[CreateAssetMenu(fileName = "CharacterModelLibrary", menuName = "Data/CharacterModelLibrary", order = 5)]
public class CharacterModelLibrary : ScriptableObject
{
    [field: SerializeField]
    public List<CharacterModelDefinition> Definitions { get; private set; }

    public bool TryGet(SurvivorAbilityType ability, out CharacterModelDefinition definition)
    {
        if (Definitions != null)
        {
            foreach (var candidate in Definitions)
            {
                if (candidate != null && candidate.Ability == ability)
                {
                    definition = candidate;
                    return true;
                }
            }
        }

        definition = null;
        return false;
    }
}

[Serializable]
public class CharacterModelDefinition
{
    [field: SerializeField]
    public SurvivorAbilityType Ability { get; private set; }

    [field: SerializeField]
    public GameObject ModelPrefab { get; private set; }

    [field: SerializeField]
    public GameObject GhostPrefab { get; private set; }

    [field: Tooltip("Optional. Falls back to ModelPrefab when unset.")]
    [field: SerializeField]
    public GameObject CorpsePrefab { get; private set; }

    [field: SerializeField]
    public RuntimeAnimatorController AnimatorController { get; private set; }

    [field: SerializeField]
    public string IdleStateName { get; private set; }

    [field: SerializeField]
    public string RunStateName { get; private set; }

    [field: SerializeField]
    public string JumpStateName { get; private set; }

    [field: SerializeField]
    public string DeathStateName { get; private set; }

    public GameObject ResolvedCorpsePrefab => CorpsePrefab != null ? CorpsePrefab : ModelPrefab;
}
