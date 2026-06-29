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
        RoundComplete,
    }

    public enum RoundWinner
    {
        None,
        Survivors,
        DungeonMaster,
    }

    public class BattleManager : NetworkSingleton<BattleManager>
    {
        public readonly SyncList<PlayerManager> Players = new();
        public int playersToStart = 1;

        [Header("Dungeon Master")]
        [SerializeField] private GameObject basicZombiePrefab;
        // Max distance the requested point is snapped onto the navmesh. Kept large so a click
        // that lands off the navmesh (on an obstacle, a wall, or past an edge) still spawns at
        // the nearest valid point instead of silently failing.
        [SerializeField] private float basicZombieSpawnSampleRadius = 50f;

        [Header("Crystal Objective")]
        [SerializeField, SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int requiredCrystals = 3;

        [SerializeField, SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int totalCrystals = 5;

        [SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int destroyedCrystals;

        [SyncVar(hook = nameof(OnRoundPhaseSynced))]
        private RoundPhase roundPhase = RoundPhase.DestroyCrystals;

        [SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int extractedSurvivors;

        [SyncVar(hook = nameof(OnObjectiveStateSynced))]
        private int requiredExtractedSurvivors;

        [SyncVar(hook = nameof(OnRoundWinnerSynced))]
        private RoundWinner winner = RoundWinner.None;

        private readonly HashSet<CrystalObjective> _crystals = new();
        private readonly HashSet<PlayerManager> _survivorsInExtraction = new();

        public event Action OnRoundStateChanged;

        public int RequiredCrystals => requiredCrystals;
        public int TotalCrystals => totalCrystals;
        public int DestroyedCrystals => destroyedCrystals;
        public RoundPhase CurrentRoundPhase => roundPhase;
        public int ExtractedSurvivors => extractedSurvivors;
        public int RequiredExtractedSurvivors => requiredExtractedSurvivors;
        public RoundWinner Winner => winner;

        private void Awake()
        {
            Startup(this);
        }

        private void OnDestroy()
        {
            DestroyInstance();
        }

        private void Update()
        {
            if (!this.isServer || this.roundPhase == RoundPhase.RoundComplete)
            {
                return;
            }

            this.ServerRefreshExtractionObjective();
        }

        [Server]
        public void ServerAddPlayer(PlayerManager player)
        {
            if (player != null && !Players.Contains(player))
            {
                Players.Add(player);
            }

            this.ServerRefreshExtractionObjective();
        }

        [Server]
        public void ServerRemovePlayer(PlayerManager player)
        {
            Players.Remove(player);
            if (player != null)
            {
                this._survivorsInExtraction.Remove(player);
            }

            this.ServerRefreshExtractionObjective();
        }

        [Server]
        public bool ServerTrySpawnBasicZombie(PlayerManager caster, Vector3 requestedPosition)
        {
            if (this.roundPhase == RoundPhase.RoundComplete)
            {
                return false;
            }

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
            this._survivorsInExtraction.Clear();
            this.destroyedCrystals = 0;
            this.extractedSurvivors = 0;
            this.requiredExtractedSurvivors = 0;
            this.winner = RoundWinner.None;
            this.roundPhase = RoundPhase.DestroyCrystals;
            this.ServerRefreshCrystalRegistry();
            this.ServerRefreshExtractionObjective();
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            this.NotifyRoundStateChanged();
        }

        [Server]
        public void ServerRegisterCrystal(CrystalObjective crystal)
        {
            if (crystal == null || !this._crystals.Add(crystal))
            {
                return;
            }

            this.totalCrystals = Mathf.Max(this.totalCrystals, this._crystals.Count);

            if (this.roundPhase != RoundPhase.DestroyCrystals)
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
            if (this.roundPhase != RoundPhase.DestroyCrystals)
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

            this.ServerRefreshExtractionObjective();
        }

        [Server]
        public void ServerSetPlayerInExtraction(PlayerManager player, bool isInside)
        {
            if (player == null)
            {
                return;
            }

            if (isInside)
            {
                this._survivorsInExtraction.Add(player);
            }
            else
            {
                this._survivorsInExtraction.Remove(player);
            }

            this.ServerRefreshExtractionObjective();
        }

        [Server]
        private void ServerCompleteRound(RoundWinner roundWinner)
        {
            if (this.roundPhase == RoundPhase.RoundComplete)
            {
                return;
            }

            this.winner = roundWinner;
            this.roundPhase = RoundPhase.RoundComplete;
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

        [Server]
        private void ServerRefreshExtractionObjective()
        {
            this._survivorsInExtraction.RemoveWhere(player => !IsRequiredForExtraction(player));

            this.requiredExtractedSurvivors = this.CountRequiredSurvivors();
            this.extractedSurvivors = Mathf.Min(
                this._survivorsInExtraction.Count,
                this.requiredExtractedSurvivors);

            if (this.roundPhase == RoundPhase.CrystalsComplete &&
                this.requiredExtractedSurvivors > 0 &&
                this.extractedSurvivors >= this.requiredExtractedSurvivors)
            {
                this.ServerCompleteRound(RoundWinner.Survivors);
            }
        }

        [Server]
        private int CountRequiredSurvivors()
        {
            int count = 0;
            foreach (var player in this.Players)
            {
                if (IsRequiredForExtraction(player))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsRequiredForExtraction(PlayerManager player)
        {
            return player != null &&
                   player.playerRole == PlayerRole.Survivor &&
                   player.player != null;
        }

        private void OnObjectiveStateSynced(int oldValue, int newValue)
        {
            this.NotifyRoundStateChanged();
        }

        private void OnRoundPhaseSynced(RoundPhase oldValue, RoundPhase newValue)
        {
            this.NotifyRoundStateChanged();
        }

        private void OnRoundWinnerSynced(RoundWinner oldValue, RoundWinner newValue)
        {
            this.NotifyRoundStateChanged();
        }

        private void NotifyRoundStateChanged()
        {
            this.OnRoundStateChanged?.Invoke(
            );
        }
    }
}
