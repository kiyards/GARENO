using System.Collections.Generic;
using Mirror;
using ProjectRuntime.Managers;
using ProjectRuntime.Network;
using TMPro;
using UnityEngine;

namespace ProjectRuntime.Actor
{
    /// <summary>
    /// A zombie disguised as a survivor: it wears the disguised survivor's character model + name and
    /// otherwise behaves exactly like <see cref="ZombieEnemy"/> (wander, chase, lunge, melee). Both the
    /// name and the model (chosen from the copied survivor's ability via <see cref="CharacterModelLibrary"/>)
    /// are decided on the server and replicated so every client shows the same disguise, including late joiners.
    /// </summary>
    public class MimicZombie : ZombieEnemy
    {
        [Header("Mimic")]
        [SerializeField] private TextMeshProUGUI nameText;

        // Drives the disguise model from the copied survivor's ability. Same asset the players use.
        [SerializeField] private CharacterModelLibrary modelLibrary;

        // The mimic's baked visual model root; replaced at runtime with the disguised survivor's model.
        [SerializeField] private Transform disguiseModelRoot;

        // Local Y for the disguise model on the mimic. The survivor models bake a ground offset tuned
        // for the player rig's origin, which differs from the mimic's, so this plants them on the mimic.
        [SerializeField] private float disguiseModelYOffset;

        // Used only when no survivor name is available (e.g. the mimic is spawned before any
        // survivor identity has synced to the server).
        private static readonly string[] FallbackNames =
        {
            "Alex", "Sam", "Jordan", "Casey", "Riley", "Morgan",
        };

        [SyncVar(hook = nameof(OnMimicNameSynced))]
        private string mimicName;

        [SyncVar(hook = nameof(OnMimicAbilitySynced))]
        private SurvivorAbilityType mimicAbility = SurvivorAbilityType.None;

        private bool _disguiseApplied;
        private SurvivorAbilityType _appliedAbility = SurvivorAbilityType.None;

        public override void OnStartServer()
        {
            base.OnStartServer();
            this.ServerApplyMimicTarget(this.ServerPickMimicTarget());
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            this.ApplyName(this.mimicName);
            this.ApplyDisguiseModel(this.mimicAbility);
        }

        // Chooses the survivor this mimic is disguised as. Returns null when there is no survivor to
        // copy (e.g. the mimic is spawned before any survivor identity has synced), in which case the
        // caller falls back to a random built-in name and no model swap.
        [Server]
        private PlayerManager ServerPickMimicTarget()
        {
            List<PlayerManager> candidates = new();
            if (BattleManager.Instance != null)
            {
                foreach (PlayerManager pm in BattleManager.Instance.Players)
                {
                    if (pm != null &&
                        pm.playerRole == PlayerRole.Survivor &&
                        !string.IsNullOrWhiteSpace(pm.playerName))
                    {
                        candidates.Add(pm);
                    }
                }
            }

            return candidates.Count > 0 ? candidates[Random.Range(0, candidates.Count)] : null;
        }

        // Snapshots the copied survivor's identity (name + ability→model) into replicated state. We
        // snapshot rather than hold a live reference so the disguise survives the copied player
        // disconnecting mid-match.
        [Server]
        private void ServerApplyMimicTarget(PlayerManager target)
        {
            this.mimicName = target != null
                ? target.playerName
                : FallbackNames[Random.Range(0, FallbackNames.Length)];
            this.mimicAbility = target != null ? target.assignedAbility : SurvivorAbilityType.None;
            this.ApplyDisguiseModel(this.mimicAbility);
        }

        private void OnMimicNameSynced(string oldValue, string newValue)
        {
            this.ApplyName(newValue);
        }

        private void OnMimicAbilitySynced(SurvivorAbilityType oldValue, SurvivorAbilityType newValue)
        {
            this.ApplyDisguiseModel(newValue);
        }

        // Swaps the mimic's visual to the disguised survivor's model + animations, keeping the mimic's
        // hit collider so it stays targetable/killable. Runs on every client (and the server) from the
        // replicated ability, so remote and late-joining clients see the same disguise.
        private void ApplyDisguiseModel(SurvivorAbilityType ability)
        {
            if (this.modelLibrary == null || this.disguiseModelRoot == null)
            {
                return;
            }

            if (this._disguiseApplied && this._appliedAbility == ability)
            {
                return;
            }

            if (!this.modelLibrary.TryGet(ability, out CharacterModelDefinition def) || def.ModelPrefab == null)
            {
                return;
            }

            Transform oldRoot = this.disguiseModelRoot;
            Transform parent = oldRoot.parent;

            GameObject newModel = Instantiate(def.ModelPrefab, parent);
            Transform newRoot = newModel.transform;
            // The survivor rig faces opposite the baked mimic model, so flip 180° to stop it moonwalking;
            // and use disguiseModelYOffset for height since the survivor's baked ground offset is tuned
            // for the player origin, not the mimic's.
            newRoot.localPosition = new Vector3(
                oldRoot.localPosition.x,
                this.disguiseModelYOffset,
                oldRoot.localPosition.z);
            newRoot.localRotation = oldRoot.localRotation * Quaternion.Euler(0f, 180f, 0f);
            newRoot.localScale = oldRoot.localScale;
            newModel.layer = oldRoot.gameObject.layer;

            // The disguise models ship no collider, but the mimic must stay hittable — re-create the
            // capsule collider that lived on the baked model onto the new model root.
            CapsuleCollider source = oldRoot.GetComponentInChildren<CapsuleCollider>(true);
            if (source != null)
            {
                CapsuleCollider clone = newModel.AddComponent<CapsuleCollider>();
                clone.center = source.center;
                clone.radius = source.radius;
                clone.height = source.height;
                clone.direction = source.direction;
                clone.isTrigger = source.isTrigger;
                clone.sharedMaterial = source.sharedMaterial;
            }

            Animator anim = newModel.GetComponentInChildren<Animator>(true);
            if (anim == null)
            {
                anim = newModel.AddComponent<Animator>();
            }

            this.disguiseModelRoot = newRoot;

            // The mimic keeps its own zombie animations (spawn/idle/walk/run/lunge/death) and only wears
            // the survivor's model — the survivor rig plays the zombie clips through its own avatar.
            this.SetRuntimeAnimator(anim);

            this.RefreshHitColliders();
            Destroy(oldRoot.gameObject);

            this._disguiseApplied = true;
            this._appliedAbility = ability;
        }

        private void ApplyName(string value)
        {
            if (this.nameText != null)
            {
                this.nameText.text = value;
            }
        }
    }
}
