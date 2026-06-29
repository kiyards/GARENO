using System;
using System.Collections.Generic;
using Core;
using Mirror;
using ProjectRuntime.Network;
using ProjectRuntime.Objectives;
using UnityEngine;
using UnityEngine.AI;

namespace ProjectRuntime.Managers
{
    public enum RoundPhase
    {
        DestroyCrystals,
        CrystalsComplete,
    }

    public class BattleManager : NetworkSingleton<BattleManager>
    {
        public readonly SyncList<PlayerManager> Players = new();
        public int playersToStart = 1;

        [Header("Dungeon Master")]
        [SerializeField] private GameObject basicZombiePrefab;
        [SerializeField] private float basicZombieSpawnSampleRadius = 2f;

        [Header("Crystal Objective")]
        [SerializeField, SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int requiredCrystals = 3;

        [SerializeField, SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int totalCrystals = 4;

        [SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int destroyedCrystals;

        [SyncVar(hook = nameof(OnRoundPhaseSynced))]
        private RoundPhase roundPhase = RoundPhase.DestroyCrystals;

        private readonly HashSet<CrystalObjective> _crystals = new();

        public event Action<int, int, RoundPhase> OnCrystalObjectiveChanged;

        public int RequiredCrystals => requiredCrystals;
        public int TotalCrystals => totalCrystals;
        public int DestroyedCrystals => destroyedCrystals;
        public RoundPhase CurrentRoundPhase => roundPhase;

        private void Awake()
        {
            Startup(this);
        }

        private void OnDestroy()
        {
            DestroyInstance();
        }

        [Server]
        public void ServerAddPlayer(PlayerManager player)
        {
            Players.Add(player);
        }

        [Server]
        public void ServerRemovePlayer(PlayerManager player)
        {
            Players.Remove(player);
        }

        [Server]
        public bool ServerTrySpawnBasicZombie(PlayerManager caster, Vector3 requestedPosition)
        {
            if (caster == null || caster.playerRole != PlayerRole.DungeonMaster)
            {
                return false;
            }

            if (this.basicZombiePrefab == null)
            {
                Debug.LogWarning("Cannot spawn Basic Zombie: no prefab assigned.");
                return false;
            }

            if (!NavMesh.SamplePosition(
                    requestedPosition,
                    out NavMeshHit navMeshHit,
                    this.basicZombieSpawnSampleRadius,
                    NavMesh.AllAreas))
            {
                return false;
            }

            var zombie = Instantiate(
                this.basicZombiePrefab,
                navMeshHit.position,
                Quaternion.identity);
            NetworkServer.Spawn(zombie);
            return true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            this._crystals.Clear();
            this.destroyedCrystals = 0;
            this.roundPhase = RoundPhase.DestroyCrystals;
            this.ServerRefreshCrystalRegistry();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            this.NotifyCrystalObjectiveChanged();
        }

        [Server]
        public void ServerRegisterCrystal(CrystalObjective crystal)
        {
            if (crystal == null || !this._crystals.Add(crystal))
            {
                return;
            }

            this.totalCrystals = Mathf.Max(this.totalCrystals, this._crystals.Count);

            if (this.roundPhase == RoundPhase.CrystalsComplete)
            {
                crystal.ServerDespawn();
            }
        }

        [Server]
        public void ServerUnregisterCrystal(CrystalObjective crystal)
        {
            if (crystal != null)
            {
                this._crystals.Remove(crystal);
            }
        }

        [Server]
        public void ServerReportCrystalDestroyed(CrystalObjective crystal)
        {
            if (crystal == null || this.roundPhase != RoundPhase.DestroyCrystals)
            {
                return;
            }

            this.ServerRefreshCrystalRegistry();
            this.destroyedCrystals = Mathf.Min(this.destroyedCrystals + 1, this.requiredCrystals);

            if (this.destroyedCrystals >= this.requiredCrystals)
            {
                this.ServerCompleteCrystalObjective();
            }
        }

        [Server]
        private void ServerCompleteCrystalObjective()
        {
            if (this.roundPhase == RoundPhase.CrystalsComplete)
            {
                return;
            }

            this.roundPhase = RoundPhase.CrystalsComplete;
            this.ServerRefreshCrystalRegistry();

            foreach (var crystal in this._crystals)
            {
                if (crystal == null || crystal.IsDespawned)
                {
                    continue;
                }

                crystal.ServerDespawn();
            }
        }

        [Server]
        private void ServerRefreshCrystalRegistry()
        {
            var crystals = FindObjectsByType<CrystalObjective>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            );

            foreach (var crystal in crystals)
            {
                if (crystal != null)
                {
                    this._crystals.Add(crystal);
                }
            }

            this.totalCrystals = Mathf.Max(this.requiredCrystals, this._crystals.Count);
        }

        private void OnObjectiveStateSynced(int oldValue, int newValue)
        {
            this.NotifyCrystalObjectiveChanged();
        }

        private void OnRoundPhaseSynced(RoundPhase oldValue, RoundPhase newValue)
        {
            this.NotifyCrystalObjectiveChanged();
        }

        private void NotifyCrystalObjectiveChanged()
        {
            this.OnCrystalObjectiveChanged?.Invoke(
                this.destroyedCrystals,
                this.requiredCrystals,
                this.roundPhase
            );
        }
    }
}
