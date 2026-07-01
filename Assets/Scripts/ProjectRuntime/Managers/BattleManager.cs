using System;
using System.Collections.Generic;
using Core;
using Mirror;
using ProjectRuntime.Actor;
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
        [SerializeField] private GameObject creeperZombiePrefab;
        // A single dog. The Group of Dogs card spawns dogPackCount copies of this prefab.
        [SerializeField] private GameObject dogPrefab;
        // Looks like a survivor with a randomized player name; AI is identical to the basic zombie.
        [SerializeField] private GameObject mimicZombiePrefab;
        // Max distance the requested point is snapped onto the navmesh. Kept large so a click
        // that lands off the navmesh (on an obstacle, a wall, or past an edge) still spawns at
        // the nearest valid point instead of silently failing.
        [SerializeField] private float basicZombieSpawnSampleRadius = 50f;

        // Group of Dogs spawns dogPackCount independent dogs around the placement point: the first
        // at the click, the rest on a ring of dogPackSpawnSpread radius. Each is NavMesh-sampled.
        [SerializeField] private int dogPackCount = 3;
        [SerializeField] private float dogPackSpawnSpread = 2f;

        [Header("Round Timer")]
        [SerializeField] private int startingRoundSeconds = 600;
        [SerializeField] private int zombieKillTimeBonusSeconds = 10;
        [SerializeField] private int survivorDownedTimePenaltySeconds = 15;
        [SerializeField] private int survivorDeathTimePenaltySeconds = 30;

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

        [SyncVar(hook = nameof(OnTimerSynced))]
        private int remainingRoundSeconds;

        private readonly HashSet<CrystalObjective> _crystals = new();
        private readonly HashSet<PlayerManager> _survivorsInExtraction = new();
        private float _timerAccumulator;
        private bool _resolvingAllDownedSurvivors;

        public event Action OnRoundStateChanged;

        public int RequiredCrystals => requiredCrystals;
        public int TotalCrystals => totalCrystals;
        public int DestroyedCrystals => destroyedCrystals;
        public RoundPhase CurrentRoundPhase => roundPhase;
        public int ExtractedSurvivors => extractedSurvivors;
        public int RequiredExtractedSurvivors => requiredExtractedSurvivors;
        public RoundWinner Winner => winner;
        public int RemainingRoundSeconds => remainingRoundSeconds;

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

            this.ServerTickRoundTimer();
            if (this.roundPhase == RoundPhase.RoundComplete)
            {
                return;
            }

            this.ServerRefreshExtractionObjective();
            this.ServerRefreshSurvivorDefeatState();
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
            return this.ServerTrySpawnEnemy(caster, requestedPosition, this.basicZombiePrefab, "Basic Zombie");
        }

        [Server]
        public bool ServerTrySpawnCreeperZombie(PlayerManager caster, Vector3 requestedPosition)
        {
            return this.ServerTrySpawnEnemy(caster, requestedPosition, this.creeperZombiePrefab, "Creeper Zombie");
        }

        [Server]
        public bool ServerTrySpawnMimicZombie(PlayerManager caster, Vector3 requestedPosition)
        {
            return this.ServerTrySpawnEnemy(caster, requestedPosition, this.mimicZombiePrefab, "Mimic Zombie");
        }

        [Server]
        public bool ServerTrySpawnGroupOfDogs(PlayerManager caster, Vector3 requestedPosition)
        {
            if (!this.ServerCanSpawnEnemy(caster, this.dogPrefab, "Group of Dogs"))
            {
                return false;
            }

            int count = Mathf.Max(1, this.dogPackCount);
            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 spawnPosition = this.GetDogPackSpawnPosition(requestedPosition, i, count);
                if (this.ServerSpawnEnemyAt(this.dogPrefab, spawnPosition))
                {
                    spawned++;
                }
            }

            // Reports success (no mana refund) as long as at least one dog landed on the navmesh.
            return spawned > 0;
        }

        // Spreads the pack around the placement point: the first dog at the center, the rest on an
        // evenly spaced ring of radius dogPackSpawnSpread. Each point is NavMesh-sampled by the
        // caller, so off-mesh ring points snap to the nearest valid spot.
        private Vector3 GetDogPackSpawnPosition(Vector3 center, int index, int count)
        {
            if (index == 0 || count <= 1)
            {
                return center;
            }

            float angle = Mathf.PI * 2f * (index - 1) / (count - 1);
            return center + new Vector3(
                Mathf.Cos(angle) * this.dogPackSpawnSpread,
                0f,
                Mathf.Sin(angle) * this.dogPackSpawnSpread);
        }

        [Server]
        private bool ServerTrySpawnEnemy(
            PlayerManager caster,
            Vector3 requestedPosition,
            GameObject prefab,
            string enemyName)
        {
            if (!this.ServerCanSpawnEnemy(caster, prefab, enemyName))
            {
                return false;
            }

            return this.ServerSpawnEnemyAt(prefab, requestedPosition);
        }

        [Server]
        private bool ServerCanSpawnEnemy(PlayerManager caster, GameObject prefab, string enemyName)
        {
            if (this.roundPhase == RoundPhase.RoundComplete)
            {
                return false;
            }

            if (caster == null || caster.playerRole != PlayerRole.DungeonMaster)
            {
                return false;
            }

            if (prefab == null)
            {
                Debug.LogWarning($"Cannot spawn {enemyName}: no prefab assigned.");
                return false;
            }

            return true;
        }

        [Server]
        private bool ServerSpawnEnemyAt(GameObject prefab, Vector3 requestedPosition)
        {
            if (!NavMesh.SamplePosition(
                    requestedPosition,
                    out NavMeshHit navMeshHit,
                    this.basicZombieSpawnSampleRadius,
                    NavMesh.AllAreas))
            {
                return false;
            }

            var enemy = Instantiate(
                prefab,
                navMeshHit.position,
                Quaternion.identity);
            NetworkServer.Spawn(enemy);
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
            this._timerAccumulator = 0f;
            this.ServerSetRemainingRoundSeconds(this.startingRoundSeconds);
            this.ServerRefreshCrystalRegistry();
            this.ServerRefreshExtractionObjective();
            this.ServerRefreshSurvivorDefeatState();
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
            this.ServerRefreshSurvivorDefeatState();
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
            this.ServerRefreshSurvivorDefeatState();
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
        private void ServerTickRoundTimer()
        {
            if (this.remainingRoundSeconds <= 0)
            {
                this.ServerCompleteRound(RoundWinner.DungeonMaster);
                return;
            }

            this._timerAccumulator += Time.deltaTime;
            while (this._timerAccumulator >= 1f &&
                   this.remainingRoundSeconds > 0 &&
                   this.roundPhase != RoundPhase.RoundComplete)
            {
                this._timerAccumulator -= 1f;
                this.ServerSetRemainingRoundSeconds(this.remainingRoundSeconds - 1);
            }

            if (this.remainingRoundSeconds <= 0)
            {
                this.ServerCompleteRound(RoundWinner.DungeonMaster);
            }
        }

        [Server]
        public void ServerAddRoundTime(int seconds)
        {
            if (seconds == 0 || this.roundPhase == RoundPhase.RoundComplete)
            {
                return;
            }

            this.ServerSetRemainingRoundSeconds(this.remainingRoundSeconds + seconds);
        }

        [Server]
        public void ServerReportZombieKilled(ZombieEnemy zombie, uint killerNetId)
        {
            if (zombie == null || this.roundPhase == RoundPhase.RoundComplete)
            {
                return;
            }

            this.ServerAddRoundTime(this.zombieKillTimeBonusSeconds);
        }

        [Server]
        public void ServerReportSurvivorDowned(PlayerManager survivor, uint sourceNetId)
        {
            if (!this.IsTimerPenaltyTarget(survivor))
            {
                return;
            }

            this.ServerAddRoundTime(-this.survivorDownedTimePenaltySeconds);
        }

        [Server]
        public void ServerReportSurvivorDied(PlayerManager survivor, uint sourceNetId)
        {
            if (!this.IsTimerPenaltyTarget(survivor))
            {
                return;
            }

            this.ServerAddRoundTime(-this.survivorDeathTimePenaltySeconds);
        }

        [Server]
        private void ServerSetRemainingRoundSeconds(int value)
        {
            int clampedValue = Mathf.Max(0, value);
            if (this.remainingRoundSeconds == clampedValue)
            {
                return;
            }

            this.remainingRoundSeconds = clampedValue;
            this.NotifyRoundStateChanged();
        }

        [Server]
        public void ServerRefreshSurvivorDefeatState()
        {
            if (this.roundPhase == RoundPhase.RoundComplete || this._resolvingAllDownedSurvivors)
            {
                return;
            }

            bool hasSurvivor = false;
            bool hasLivingSurvivor = false;
            bool allLivingSurvivorsDowned = true;
            foreach (var player in this.Players)
            {
                if (player == null || player.playerRole != PlayerRole.Survivor)
                {
                    continue;
                }

                hasSurvivor = true;
                if (player.lives <= 0)
                {
                    continue;
                }

                hasLivingSurvivor = true;
                if (player.player == null || !player.player.IsDowned)
                {
                    allLivingSurvivorsDowned = false;
                    return;
                }
            }

            if (hasLivingSurvivor && allLivingSurvivorsDowned)
            {
                this.ServerResolveAllDownedSurvivors();
                return;
            }

            if (hasSurvivor && !hasLivingSurvivor)
            {
                this.ServerCompleteRound(RoundWinner.DungeonMaster);
            }
        }

        [Server]
        private void ServerResolveAllDownedSurvivors()
        {
            this._resolvingAllDownedSurvivors = true;

            foreach (var player in this.Players)
            {
                if (player == null ||
                    player.playerRole != PlayerRole.Survivor ||
                    player.lives <= 0 ||
                    player.player == null ||
                    !player.player.IsDowned)
                {
                    continue;
                }

                // Resolve everyone as if their revive window timed out.
                player.player.ServerResolveDowned(2);
            }

            this._resolvingAllDownedSurvivors = false;
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
            // Permanently dead survivors (no lives left) are not required to extract; downed ones
            // (still have lives) are.
            return player != null &&
                   player.playerRole == PlayerRole.Survivor &&
                   player.player != null &&
                   player.lives > 0;
        }

        private static bool IsActiveSurvivor(PlayerManager player)
        {
            // "Active" = still in the fight. A downed survivor (lives > 0) counts — the DM only wins
            // once every survivor is permanently dead, not merely downed. Lives is the source of truth.
            return player != null &&
                   player.playerRole == PlayerRole.Survivor &&
                   player.lives > 0;
        }

        private bool IsTimerPenaltyTarget(PlayerManager player)
        {
            return this.roundPhase != RoundPhase.RoundComplete &&
                   player != null &&
                   player.playerRole == PlayerRole.Survivor;
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

        private void OnTimerSynced(int oldValue, int newValue)
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
