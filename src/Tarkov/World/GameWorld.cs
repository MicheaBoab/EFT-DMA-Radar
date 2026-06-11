/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Misc;
using System.Globalization;
using System.Linq;
using LoneEftDmaRadar.Misc.Workers;
using LoneEftDmaRadar.Tarkov.IL2CPP;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.Tarkov.World.Exits;
using LoneEftDmaRadar.Tarkov.World.Explosives;
using LoneEftDmaRadar.Tarkov.World.Hazards;
using LoneEftDmaRadar.Tarkov.World.Loot;
using LoneEftDmaRadar.Tarkov.World.Player;
using LoneEftDmaRadar.Tarkov.World.Player.Helpers;
using LoneEftDmaRadar.Tarkov.World.Quests;
using VmmSharpEx.Extensions;
using VmmSharpEx.Options;

namespace LoneEftDmaRadar.Tarkov.World
{
    /// <summary>
    /// Class containing Game (Raid) instance.
    /// IDisposable.
    /// </summary>
    public sealed class GameWorld : IDisposable
    {
        #region Fields / Properties / Constructors

        public static implicit operator ulong(GameWorld x) => x.Base;

        private static EftDmaConfig Config { get; } = Program.Config;

        /// <summary>
        /// World Address.
        /// </summary>
        private ulong Base { get; }

        private readonly CancellationTokenSource _cts = new();
        private readonly RegisteredPlayers _rgtPlayers;
        private readonly ExplosivesManager _explosivesManager;
        private readonly WorkerThread _t1;
        private readonly WorkerThread _t2;
        private readonly WorkerThread _t3;

        /// <summary>
        /// Map ID of Current Map.
        /// </summary>
        public string MapID { get; }

        public bool InRaid => !_disposed;
        public IReadOnlyCollection<AbstractPlayer> Players => _rgtPlayers;
        public IReadOnlyCollection<IExplosiveItem> Explosives => _explosivesManager;
        public LocalPlayer LocalPlayer => _rgtPlayers.LocalPlayer;
        public LootManager Loot { get; }
        public QuestManager QuestManager { get; }
        public IReadOnlyCollection<IExitPoint> Exits { get; }
        public IReadOnlyCollection<IWorldHazard> Hazards { get; }
        public bool RaidStarted { get; private set; }
        /// <summary>
        /// UTC timestamp when raid was detected as started. Null if raid not started yet.
        /// </summary>
        public DateTime? RaidStartedAt { get; private set; }

        private GameWorld() { }

        /// <summary>
        /// Game Constructor.
        /// Only called internally.
        /// </summary>
        private GameWorld(ulong gameWorld, string mapID)
        {
            try
            {
                Base = gameWorld;
                MapID = mapID;
                _t1 = new WorkerThread()
                {
                    Name = "Realtime Worker",
                    ThreadPriority = ThreadPriority.AboveNormal,
                    SleepDuration = TimeSpan.FromMilliseconds(8),
                    SleepMode = WorkerThreadSleepMode.DynamicSleep
                };
                _t1.PerformWork += RealtimeWorker_PerformWork;
                _t2 = new WorkerThread()
                {
                    Name = "Slow Worker",
                    ThreadPriority = ThreadPriority.BelowNormal,
                    SleepDuration = TimeSpan.FromMilliseconds(50)
                };
                _t2.PerformWork += SlowWorker_PerformWork;
                _t3 = new WorkerThread()
                {
                    Name = "Explosives Worker",
                    SleepDuration = TimeSpan.FromMilliseconds(30),
                    SleepMode = WorkerThreadSleepMode.DynamicSleep
                };
                _t3.PerformWork += ExplosivesWorker_PerformWork;
                var rgtPlayersAddr = Memory.ReadPtr(gameWorld + Offsets.GameWorld.RegisteredPlayers, false);
                _rgtPlayers = new RegisteredPlayers(rgtPlayersAddr, this);
                ArgumentOutOfRangeException.ThrowIfLessThan(_rgtPlayers.GetPlayerCount(), 1, nameof(_rgtPlayers));
                QuestManager = new(_rgtPlayers.LocalPlayer.Profile);
                Loot = new(gameWorld);
                _explosivesManager = new(gameWorld);
                Hazards = GetHazards(MapID);
                Exits = GetExits(MapID, _rgtPlayers.LocalPlayer.IsPmc);
                // Ensure Cache
                Config.Cache.RaidCache ??= new();
                if (Config.Cache.RaidCache.GameWorld != gameWorld)
                {
                    Config.Cache.RaidCache = new()
                    {
                        GameWorld = gameWorld
                    };
                }
                // Check if raid already started
                RaidStarted = _rgtPlayers.LocalPlayer.CheckIsRaidStarted() ?? false;
                if (RaidStarted)
                {
                    Logging.WriteLine("[GameWorld] Raid has already started!");
                    // Record raid start timestamp so later logic can enforce the grouping lock window
                    RaidStartedAt = DateTime.UtcNow;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static List<IWorldHazard> GetHazards(string mapId)
        {
            var list = new List<IWorldHazard>();
            if (TarkovDataManager.MapData.TryGetValue(mapId, out var mapData))
            {
                foreach (var hazard in mapData.Hazards)
                {
                    list.Add(hazard);
                }
            }
            return list;
        }

        private static List<IExitPoint> GetExits(string mapId, bool isPMC)
        {
            var list = new List<IExitPoint>();
            if (TarkovDataManager.MapData.TryGetValue(mapId, out var mapData))
            {
                var filteredExfils = isPMC ?
                    mapData.Extracts.Where(x => x.IsShared || x.IsPmc) :
                    mapData.Extracts.Where(x => !x.IsPmc);
                foreach (var exfil in filteredExfils)
                {
                    list.Add(new Exfil(exfil));
                }
                foreach (var transit in mapData.Transits)
                {
                    list.Add(new TransitPoint(transit));
                }
            }
            return list;
        }

        /// <summary>
        /// Start all Game Threads.
        /// </summary>
        public void Start()
        {
            _t1.Start();
            _t2.Start();
            _t3.Start();
        }

        /// <summary>
        /// Blocks until a World Singleton Instance can be instantiated.
        /// </summary>
        public static GameWorld CreateGameInstance()
        {
            while (true)
            {
                Memory.ThrowIfProcessNotRunning();
                try
                {
                    var instance = GetGameWorld();
                    Logging.WriteLine($"Valid GameWorld Found! {instance}");
                    return instance;
                }
                catch (Exception ex)
                {
                    Logging.WriteLine($"ERROR Instantiating Game Instance: {ex}");
                }
                finally
                {
                    Thread.Sleep(1000);
                }
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Checks if a Raid has started.
        /// Loads Game World resources.
        /// </summary>
        /// <returns>True if Raid has started, otherwise False.</returns>
        private static GameWorld GetGameWorld()
        {
            try
            {
                Lookup.Find(out ulong gameWorld, out string map);
                return new GameWorld(gameWorld, map);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("ERROR Getting GameWorld", ex);
            }
        }

        /// <summary>
        /// Main Game Loop executed by Memory Worker Thread. Refreshes/Updates Player List and performs Player Allocations.
        /// </summary>
        public void Refresh()
        {
            ThrowIfRaidEnded();
            ProcessBTR();
            _rgtPlayers.Refresh(); // Check for new players, add to list, etc.
        }

        /// <summary>
        /// Request a Radar Restart by terminating the current instance.
        /// </summary>
        public void Restart()
        {
            _cts.Cancel();
        }

        /// <summary>
        /// Throws an exception if the current raid instance should be terminated.
        /// </summary>
        /// <exception cref="OperationCanceledException"></exception>
        /// <exception cref="RaidEndedException"></exception>
        private void ThrowIfRaidEnded()
        {
            _cts.Token.ThrowIfCancellationRequested(); // Check if user requested radar restart
            for (int i = 0; i < 5; i++) // Re-attempt if read fails -- 5 times
            {
                try
                {
                    if (IsRaidActive())
                        return;
                }
                catch { }
                Thread.Sleep(67); // Small delay before retry
            }
            throw new RaidEndedException(); // Still not valid? Raid must have ended.
        }

        /// <summary>
        /// Checks if the Current Raid is Active, and LocalPlayer is alive/active.
        /// </summary>
        /// <returns>True if raid is active, otherwise False.</returns>
        private bool IsRaidActive()
        {
            try
            {
                var mainPlayer = Memory.ReadPtr(this + Offsets.GameWorld.MainPlayer, false);
                ArgumentOutOfRangeException.ThrowIfNotEqual(mainPlayer, _rgtPlayers.LocalPlayer, nameof(mainPlayer));
                return _rgtPlayers.GetPlayerCount() > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Processes BTR Vehicle and allocates BTR Player if found.
        /// No-op if map is not Streets/Woods, or if BTR Player already allocated.
        /// </summary>
        private void ProcessBTR()
        {
            try
            {
                // Check if we should process
                if (!(MapID.Equals("tarkovstreets", StringComparison.OrdinalIgnoreCase) ||
                    MapID.Equals("woods", StringComparison.OrdinalIgnoreCase)) ||
                    _rgtPlayers.Any(p => p is BtrPlayer))
                {
                    return;
                }
                // OK -> Process
                var btrController = Memory.ReadPtr(this + Offsets.GameWorld.BtrController);
                var btrView = Memory.ReadPtr(btrController + Offsets.BtrController.BtrView);
                var btrTurretView = Memory.ReadPtr(btrView + Offsets.BTRView.turret);
                var btrOperator = Memory.ReadPtr(btrTurretView + Offsets.BTRTurretView._bot);
                _rgtPlayers.TryAllocateBTR(btrView, btrOperator);
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"ERROR Allocating BTR: {ex}");
            }
        }

        #endregion

        #region Realtime Thread T1

        /// <summary>
        /// Managed Worker Thread that does realtime (player position/info) updates.
        /// </summary>
        private void RealtimeWorker_PerformWork(object sender, WorkerThreadArgs e)
        {
            bool hasPlayers = false;

            using var scatter = Memory.CreateScatter(VmmFlags.NOCACHE);
            foreach (var player in _rgtPlayers)
            {
                if (player.IsActive && player.IsAlive)
                {
                    hasPlayers = true;
                    player.OnRealtimeLoop(scatter);
                }
            }

            if (!hasPlayers)
            {
                Thread.Sleep(1);
                return;
            }

            scatter.Execute();
        }

        #endregion

        #region Slow Thread T2

        /// <summary>
        /// Managed Worker Thread that does ~Slow Game World Updates.
        /// *** THIS THREAD HAS A LONG RUN TIME! LOOPS ~MAY~ TAKE ~10 SECONDS OR MORE ***
        /// </summary>
        private void SlowWorker_PerformWork(object sender, WorkerThreadArgs e)
        {
            var ct = e.CancellationToken;
            ValidatePlayerTransforms(); // Check for transform anomalies
            Loot.Refresh(ct);
            if (Config.Loot.ShowWishlist)
                Memory.LocalPlayer?.RefreshWishlist(ct);
            RefreshQuestHelper(ct);
            PreRaidStartChecks(ct);

            // Note: Auto-grouping is only performed at raid start or when a new player is allocated.
            // Do not perform grouping or reapplication here to avoid dynamic grouping while players move.
        }

        /// <summary>
        /// Executes pre-raid start checks to determine if the raid has started, and various child operations.
        /// </summary>
        /// <param name="ct"></param>
        private void PreRaidStartChecks(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (RaidStarted || this.LocalPlayer is not LocalPlayer localPlayer)
                return;
            try
            {
                RaidStarted = localPlayer.CheckIsRaidStarted() ??
                    throw new InvalidOperationException("Unable to get Hands Data!");
                if (RaidStarted)
                {
                    Logging.WriteLine("[PreRaidStartChecks] Raid has started!");
                    // Record when the raid was detected as started
                    RaidStartedAt = DateTime.UtcNow;
                }
                if (!RaidStarted && !localPlayer.IsScav)
                {
                    RefreshSpecialAi(ct);
                    if (Config.Misc.AutoGroups)
                    {
                        RefreshGroups(localPlayer, ct);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[PreRaidStartChecks] ERROR: {ex}");
            }
        }

        /// <summary>
        /// Refreshes AI Types for all Special AI players, including but not limited to:
        /// Santa, Guards, etc.
        /// </summary>
        private void RefreshSpecialAi(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            const float guardDistanceThreshold = 15f;

            var aiPlayers = _rgtPlayers.Where(p => p.IsAI && p.Position.IsNormal())
                .OfType<ObservedPlayer>()
                .ToList();

            var bossPositions = aiPlayers
                .Where(p => p.Type == PlayerType.AIBoss)
                .Select(p => p.Position)
                .ToList();

            // Iterate all AI
            foreach (var ai in aiPlayers)
            {
                ct.ThrowIfCancellationRequested();
                if (ai.GetSpecialAiRole() is AIRole specialRole) // Santa, etc.
                {
                    ai.AssignSpecialAiRole(specialRole);
                }
                else if (ai.Type == PlayerType.AIScav || ai.Name == "Guard") // Guards
                {
                    bool isGuard = false;
                    foreach (var bossPos in bossPositions)
                    {
                        if (Vector3.Distance(ai.Position, bossPos) <= guardDistanceThreshold)
                        {
                            isGuard = true;
                            break;
                        }
                    }

                    if (isGuard)
                    {
                        ai.AssignSpecialAiRole(new("Guard", PlayerType.AIRaider));
                    }
                    else
                    {
                        ai.AssignSpecialAiRole(null);
                    }
                }
            }
        }

        /// <summary>
        /// Refreshes Player Groups based on proximity to each other before raid start.
        /// </summary>
        /// <param name="localPlayer"></param>
        /// <param name="ct"></param>
        private void RefreshGroups(LocalPlayer localPlayer, CancellationToken ct, bool allowForming = true)
        {
            ct.ThrowIfCancellationRequested();

            // Grouping should only be performed at raid start or when a player connects.
            // Do NOT form groups based on runtime proximity (players moving within 30m).
            const float spawnDistanceThreshold = 30f;

            // Build new assignments in a local dict
            var newGroups = new ConcurrentDictionary<int, int>();

            // Snapshot of previous groups for preservation logic when not forming new groups
            var oldGroups = Config.Cache.RaidCache?.Groups ?? new ConcurrentDictionary<int, int>();

            // Collect all valid human pmc players
            var players = _rgtPlayers
                .Where(p => p.IsHuman && p.IsPmc)
                .OfType<ObservedPlayer>()
                .ToList();

            if (players.Count == 0)
            {
                // No players - replace with empty dict
                Config.Cache.RaidCache.Groups = newGroups;
                return;
            }

            if (!allowForming)
            {
                // Runtime check: do NOT form or dissolve groups. Simply reapply the
                // existing cached group assignments to the currently allocated players.
                try
                {
                    foreach (var pl in _rgtPlayers.OfType<ObservedPlayer>().Where(p => p.IsHuman && p.IsPmc))
                    {
                        if (oldGroups.TryGetValue(pl.Id, out var gid))
                        {
                            if (gid == AbstractPlayer.TeammateGroupId)
                                pl.AssignTeammate(true);
                            else
                            {
                                pl.AssignTeammate(false);
                                pl.AssignGroup(gid);
                            }
                        }
                        else
                        {
                            // No cached group -> leave as solo
                            pl.AssignTeammate(false);
                            pl.AssignGroup(AbstractPlayer.SoloGroupId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logging.WriteLine($"[RefreshGroups] Reapply cached groups ERROR: {ex}");
                }

                // Do not modify the persisted group map during runtime checks
                return;
            }

            // allowForming == true : full grouping behavior (form groups at spawn/init)
            // Preserve existing groups: start with the oldGroups so we do not dissolve
            // previously-formed groups. Assign or extend groups for current components
            // but do not overwrite existing assignments with Solo.
            foreach (var kv in oldGroups)
            {
                newGroups[kv.Key] = kv.Value;
            }

            // Determine nextGroupId based on existing groups to avoid collisions
            int nextGroupId = 1;
            int maxExisting = oldGroups.Values
                .Where(v => v > 0 && v != AbstractPlayer.TeammateGroupId && v != AbstractPlayer.SoloGroupId)
                .DefaultIfEmpty(0)
                .Max();
            nextGroupId = Math.Max(nextGroupId, maxExisting + 1);

            var spawnPositions = Config.Cache.RaidCache?.SpawnPositions;

            // If spawn positions are available, form groups by spawn proximity only.
            if (spawnPositions is not null && spawnPositions.Count > 0)
            {
                // Build map of player id -> spawn position (when available)
                var spawnMap = new Dictionary<int, Vector3>();
                foreach (var p in players)
                {
                    try
                    {
                        if (spawnPositions.TryGetValue(p.Id, out var s) && !string.IsNullOrEmpty(s))
                        {
                            var parts = s.Split(';');
                            if (parts.Length == 3 &&
                                float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sx) &&
                                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var sy) &&
                                float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var sz))
                            {
                                spawnMap[p.Id] = new Vector3(sx, sy, sz);
                            }
                        }
                    }
                    catch { }
                }

                // Cluster by spawn proximity (no runtime position checks)
                var visited = new HashSet<int>();
                foreach (var p in players)
                {
                    ct.ThrowIfCancellationRequested();
                    if (visited.Contains(p.Id))
                        continue;

                    // If this player has no spawn recorded, skip forming new group for them
                    if (!spawnMap.TryGetValue(p.Id, out var pSpawn))
                    {
                        // Keep existing or mark as solo
                        if (!newGroups.ContainsKey(p.Id))
                        {
                            newGroups[p.Id] = AbstractPlayer.SoloGroupId;
                            p.AssignTeammate(false);
                            p.AssignGroup(AbstractPlayer.SoloGroupId);
                        }
                        visited.Add(p.Id);
                        continue;
                    }

                    // Gather all players whose spawn is within threshold of this player's spawn
                    var component = new List<ObservedPlayer> { p };
                    visited.Add(p.Id);
                    foreach (var q in players)
                    {
                        if (visited.Contains(q.Id))
                            continue;
                        if (!spawnMap.TryGetValue(q.Id, out var qSpawn))
                            continue;
                        if (Vector3.Distance(pSpawn, qSpawn) <= spawnDistanceThreshold)
                        {
                            component.Add(q);
                            visited.Add(q.Id);
                        }
                    }

                    // Determine if component contains local (based on local player's current position)
                    bool containsLocal = component.Any(c => Vector3.Distance(c.Position, localPlayer.Position) <= spawnDistanceThreshold);

                    if (containsLocal)
                    {
                        foreach (var member in component)
                        {
                            newGroups[member.Id] = AbstractPlayer.TeammateGroupId;
                            member.AssignTeammate(true);
                        }
                        continue;
                    }

                    if (component.Count < 2)
                    {
                        var single = component[0];
                        if (!newGroups.ContainsKey(single.Id))
                        {
                            newGroups[single.Id] = AbstractPlayer.SoloGroupId;
                            single.AssignTeammate(false);
                            single.AssignGroup(AbstractPlayer.SoloGroupId);
                        }
                        continue;
                    }

                    // Multi-player hostile group: reuse existing group id if any member has one
                    int existingGroup = component.Select(m => oldGroups.TryGetValue(m.Id, out var g) ? g : AbstractPlayer.SoloGroupId)
                        .FirstOrDefault(g => g != AbstractPlayer.SoloGroupId && g != AbstractPlayer.TeammateGroupId);

                    int groupId = existingGroup != 0 ? existingGroup : nextGroupId++;
                    foreach (var member in component)
                    {
                        newGroups[member.Id] = groupId;
                        member.AssignTeammate(false);
                        member.AssignGroup(groupId);
                    }
                }
            }
            else
            {
                // No spawn info available - preserve existing groups but do not form new proximity groups.
                foreach (var p in players)
                {
                    if (oldGroups.TryGetValue(p.Id, out var gid))
                    {
                        if (gid == AbstractPlayer.TeammateGroupId)
                            p.AssignTeammate(true);
                        else
                        {
                            p.AssignTeammate(false);
                            p.AssignGroup(gid);
                        }
                        newGroups[p.Id] = gid;
                    }
                    else
                    {
                        newGroups[p.Id] = AbstractPlayer.SoloGroupId;
                        p.AssignTeammate(false);
                        p.AssignGroup(AbstractPlayer.SoloGroupId);
                    }
                }
            }

            // Atomic replacement - swap the entire dict reference
            Config.Cache.RaidCache.Groups = newGroups;
        }

        /// <summary>
        /// Public wrapper to trigger proximity-based auto grouping from external callers.
        /// </summary>
        public void UpdateAutoGroups(bool allowForming = false)
        {
            try
            {
                if (Config.Misc.AutoGroups && _rgtPlayers.LocalPlayer is LocalPlayer local)
                {
                    // If groups are manually locked via UI, always reapply cached groups and do not form new ones.
                    if (Config.Cache.RaidCache?.GroupsLocked == true)
                    {
                        Logging.WriteLine("[UpdateAutoGroups] Groups are manually locked - reapplying cached groups.");
                        RefreshGroups(local, CancellationToken.None, allowForming: false);
                        return;
                    }
                    // If the raid has been started for longer than the lock window, do not allow forming
                    var lockWindow = TimeSpan.FromSeconds(30);
                    if (allowForming && RaidStartedAt.HasValue && DateTime.UtcNow - RaidStartedAt.Value > lockWindow)
                    {
                        // Prevent forming new groups after the lock window elapsed; just reapply cached groups
                        Logging.WriteLine("[UpdateAutoGroups] Group forming disabled - lock window elapsed.");
                        RefreshGroups(local, CancellationToken.None, allowForming: false);
                    }
                    else
                    {
                        RefreshGroups(local, CancellationToken.None, allowForming);
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"[UpdateAutoGroups] ERROR: {ex}");
            }
        }

        private void RefreshQuestHelper(CancellationToken ct)
        {
            if (Config.QuestHelper.Enabled)
            {
                QuestManager.Refresh(ct);
            }
        }

        public void ValidatePlayerTransforms()
        {
            try
            {
                using var map = Memory.CreateScatterMap();
                var round1 = map.AddRound();
                var round2 = map.AddRound();
                bool hasPlayers = false;

                foreach (var player in _rgtPlayers)
                {
                    if (player.IsActive && player.IsAlive && player is not BtrPlayer)
                    {
                        hasPlayers = true;
                        player.OnValidateTransforms(round1, round2);
                    }
                }

                if (hasPlayers)
                    map.Execute();
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"CRITICAL ERROR - ValidatePlayerTransforms Loop FAILED: {ex}");
            }
        }

        #endregion

        #region Explosives Thread T3

        /// <summary>
        /// Managed Worker Thread that does Explosives (grenades,etc.) updates.
        /// </summary>
        private void ExplosivesWorker_PerformWork(object sender, WorkerThreadArgs e)
        {
            _explosivesManager.Refresh(e.CancellationToken);
        }

        #endregion

        #region IDisposable

        private bool _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, true) == false)
            {
                _cts.Dispose();
                _t1?.Dispose();
                _t2?.Dispose();
                _t3?.Dispose();
            }
        }

        #endregion

        #region Misc

        public sealed class RaidEndedException : Exception
        {
            public RaidEndedException() : base() { }
        }

        public override string ToString()
        {
            return $"GameWorld:{Base:X}";
        }

        /// <summary>
        /// Contains methods to lookup GameWorld instance.
        /// </summary>
        private static class Lookup
        {
            public static void Find(out ulong gameWorld, out string map)
            {
                Logging.WriteLine("Searching for GameWorld...");

                using var searchCts = new CancellationTokenSource();
                try
                {
                    Task<GameWorldResult> winner = null;
                    var tasks = new List<Task<GameWorldResult>>()
                    {
                        Task.Run(() => FindViaIL2CPP(searchCts.Token)),
                        Task.Run(() => FindViaGOM(searchCts.Token))
                    };

                    while (tasks.Count > 1) // IL2CPP will never exit normally
                    {
                        var finished = Task.WhenAny(tasks).GetAwaiter().GetResult();
                        tasks.Remove(finished);

                        if (finished.Status == TaskStatus.RanToCompletion)
                        {
                            winner = finished;
                            break;
                        }
                    }

                    if (winner is null)
                        throw new InvalidOperationException("GameWorld not found.");

                    gameWorld = winner.Result.GameWorld;
                    map = winner.Result.Map;
                }
                finally
                {
                    searchCts.Cancel();
                }
            }

            /// <summary>
            /// Finds GameWorld using IL2CPP interop.
            /// </summary>
            private static GameWorldResult FindViaIL2CPP(CancellationToken ct1)
            {
                while (true)
                {
                    ct1.ThrowIfCancellationRequested();
                    try
                    {
                        if (IL2CPPLib.TryGetGameWorld(out ulong gameWorld, out string map))
                        {
                            Logging.WriteLine("GameWorld Found! (IL2CPP)");
                            return new GameWorldResult
                            {
                                GameWorld = gameWorld,
                                Map = map
                            };
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                    Thread.Sleep(10);
                }
            }

            /// <summary>
            /// Finds GameWorld using Unity GameObjectManager with parallel subtasks.
            /// </summary>
            private static GameWorldResult FindViaGOM(CancellationToken ct)
            {
                var gom = GameObjectManager.Get();
                var firstObject = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
                var lastObject = Memory.ReadValue<LinkedListObject>(gom.LastActiveNode);
                firstObject.ThisObject.ThrowIfInvalidUserVA(nameof(firstObject));
                firstObject.NextObjectLink.ThrowIfInvalidUserVA(nameof(firstObject));
                lastObject.ThisObject.ThrowIfInvalidUserVA(nameof(lastObject));

                using var gomCts = new CancellationTokenSource();
                try
                {
                    Task<GameWorldResult> winner = null;
                    var tasks = new List<Task<GameWorldResult>>()
                    {
                        Task.Run(() => GOM_ReadShallow(gomCts.Token, ct)),
                        Task.Run(() => GOM_ReadForward(firstObject, lastObject, gomCts.Token, ct))
                    };

                    while (tasks.Count > 1) // Shallow will never exit normally
                    {
                        var finished = Task.WhenAny(tasks).GetAwaiter().GetResult();
                        ct.ThrowIfCancellationRequested();
                        tasks.Remove(finished);

                        if (finished.Status == TaskStatus.RanToCompletion)
                        {
                            winner = finished;
                            break;
                        }
                    }

                    if (winner is null)
                        throw new InvalidOperationException("GameWorld not found via GOM.");

                    return winner.Result;
                }
                finally
                {
                    gomCts.Cancel();
                }
            }

            private static GameWorldResult GOM_ReadShallow(CancellationToken gomCt, CancellationToken ct)
            {
                const int maxDepth = 10000;
                while (true)
                {
                    gomCt.ThrowIfCancellationRequested();
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // This implementation is completely self-contained to keep memory state fresh on re-loops
                        var gom = GameObjectManager.Get();
                        var currentObject = Memory.ReadValue<LinkedListObject>(gom.ActiveNodes);
                        int iterations = 0;
                        while (currentObject.ThisObject.IsValidUserVA())
                        {
                            gomCt.ThrowIfCancellationRequested();
                            ct.ThrowIfCancellationRequested();
                            if (iterations++ >= maxDepth)
                                break;
                            if (ParseGameWorldGameObject(ref currentObject) is GameWorldResult result)
                            {
                                Logging.WriteLine("GameWorld Found! (GOM Shallow)");
                                return result;
                            }

                            currentObject = Memory.ReadValue<LinkedListObject>(currentObject.NextObjectLink); // Read next object
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                }
            }

            private static GameWorldResult GOM_ReadForward(LinkedListObject currentObject, LinkedListObject lastObject, CancellationToken gomCt, CancellationToken ct)
            {
                while (currentObject.ThisObject != lastObject.ThisObject)
                {
                    gomCt.ThrowIfCancellationRequested();
                    ct.ThrowIfCancellationRequested();
                    if (ParseGameWorldGameObject(ref currentObject) is GameWorldResult result)
                    {
                        Logging.WriteLine("GameWorld Found! (GOM Forward)");
                        return result;
                    }

                    currentObject = Memory.ReadValue<LinkedListObject>(currentObject.NextObjectLink); // Read next object
                }
                throw new InvalidOperationException("GameWorld not found.");
            }

            private static GameWorldResult ParseGameWorldGameObject(ref LinkedListObject gameObject)
            {
                try
                {
                    gameObject.ThisObject.ThrowIfInvalidUserVA(nameof(gameObject));
                    var objectNamePtr = Memory.ReadPtr(gameObject.ThisObject + UnityOffsets.GameObject_NameOffset);
                    var objectNameStr = Memory.ReadUtf8String(objectNamePtr, 64);
                    if (objectNameStr.Equals("GameWorld", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var gameWorld = Memory.ReadPtrChain(gameObject.ThisObject, true, UnityOffsets.GameWorldChain);
                            /// Get Selected Map
                            var mapPtr = Memory.ReadValue<ulong>(gameWorld + Offsets.GameWorld.LocationId);
                            if (mapPtr == 0x0) // Offline Mode
                            {
                                var localPlayer = Memory.ReadPtr(gameWorld + Offsets.GameWorld.MainPlayer);
                                mapPtr = Memory.ReadPtr(localPlayer + Offsets.Player.Location);
                            }

                            string map = Memory.ReadUnityString(mapPtr, 128);
                            Logging.WriteLine("Detected Map " + map);
                            if (!TarkovDataManager.MapData.ContainsKey(map)) // Also makes sure we're not in the hideout
                                throw new ArgumentException("Invalid Map ID!");
                            return new GameWorldResult()
                            {
                                GameWorld = gameWorld,
                                Map = map
                            };
                        }
                        catch (Exception ex)
                        {
                            Logging.WriteLine($"Invalid GameWorld Instance: {ex}");
                        }
                    }
                }
                catch { }
                return null;
            }

            private class GameWorldResult
            {
                public ulong GameWorld { get; init; }
                public string Map { get; init; }
            }
        }

        #endregion
    }
}
