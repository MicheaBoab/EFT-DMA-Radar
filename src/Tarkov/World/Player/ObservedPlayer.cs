/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.Tarkov.World.Player.Helpers;
using LoneEftDmaRadar.Web.TarkovDev;
using System.Collections.Frozen;
using System.Text;
using VmmSharpEx.Scatter;

namespace LoneEftDmaRadar.Tarkov.World.Player
{
    public class ObservedPlayer : AbstractPlayer
    {
        private readonly GameWorld _gameWorld;
        private readonly RaidCache _raidCache;
        private bool _isUsecBearAi;
        private string _usecBearAiFactionName;
        private DateTime _lastGroupRefresh;
        private bool _specialAiPostInitFinalized;
        /// <summary>
        /// Player's Unique Id within this Raid Instance [Human Players Only].
        /// </summary>
        public int Id { get; }
        private string _specialName; // Backing field for special roles
        /// <summary>
        /// Player's In-Game Name as displayed on the Radar.
        /// </summary>
        public override string Name
        {
            get => _specialName ?? base.Name;
            protected set => base.Name = value;
        }

        private void TryRefreshHumanGroupFromView()
        {
            try
            {
                if (!IsHuman)
                    return;
                string grp = ReadGroupString();
                if (string.IsNullOrEmpty(grp))
                    return;
                // Map group string to numeric id - try to reuse existing cache if possible
                // If the local player's profile parsing stores numeric GroupIds elsewhere, that mapping should be used.
                // For now, if non-empty group string exists, mark as teammate if it matches local player's group string
                var local = Memory.LocalPlayer as LocalPlayer;
                if (local is not null)
                {
                    var localGroup = Memory.ReadUnityString(local.Profile + Offsets.PlayerInfo.GroupId);
                    if (!string.IsNullOrEmpty(localGroup) && localGroup == grp)
                    {
                        AssignTeammate(true);
                        return;
                    }
                }
                // Otherwise, keep as PMC/PScav with SoloGroupId unless cache has mapping
                if (Config.Cache.RaidCache is RaidCache rc && rc.Groups.TryGetValue(Id, out var gid))
                {
                    SetGroupId(gid);
                }
            }
            catch { }
        }
        private PlayerType? _specialType; // Backing field for special roles
        /// <summary>
        /// Player's Type.
        /// </summary>
        public override PlayerType Type
        {
            get => _specialType ?? base.Type;
            protected set => base.Type = value;
        }
        /// <summary>
        /// Player's Current TarkovDevItems.
        /// </summary>
        public PlayerEquipment Equipment { get; }
        /// <summary>
        /// Address of InventoryController field.
        /// </summary>
        public ulong InventoryControllerAddr { get; }
        /// <summary>
        /// Hands Controller field address.
        /// </summary>
        public ulong HandsControllerAddr { get; }
        /// <summary>
        /// ObservedPlayerController for non-clientplayer players.
        /// </summary>
        private ulong ObservedPlayerController { get; }
        /// <summary>
        /// ObservedHealthController for non-clientplayer players.
        /// </summary>
        private ulong ObservedHealthController { get; }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; }
        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public override ulong MovementContext { get; }
        /// <summary>
        /// Corpse field address..
        /// </summary>
        public override ulong CorpseAddr { get; }
        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public override ulong RotationAddress { get; }
        /// <summary>
        /// Player's Current Health Status
        /// </summary>
        public Enums.ETagStatus HealthStatus { get; private set; } = Enums.ETagStatus.Healthy;

        /// <summary>
        /// True when this AI was initially identified as USEC/BEAR-type AI voice profile.
        /// </summary>
        public bool IsUsecBearAi => _isUsecBearAi;

        /// <summary>
        /// USEC/BEAR faction display name for AI PMC-like units.
        /// </summary>
        public string UsecBearAiFactionName => _usecBearAiFactionName;

        internal ObservedPlayer(ulong playerBase, GameWorld gameWorld) : base(playerBase)
        {
            _gameWorld = gameWorld!;
            _raidCache = Config.Cache.RaidCache!; // Populate Raid Cache Access
            ObservedPlayerController = Memory.ReadPtr(this + Offsets.ObservedPlayerView.ObservedPlayerController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this,
                Memory.ReadValue<ulong>(ObservedPlayerController + Offsets.ObservedPlayerController.PlayerView),
                nameof(ObservedPlayerController));
            InventoryControllerAddr = ObservedPlayerController + Offsets.ObservedPlayerController.InventoryController;
            HandsControllerAddr = ObservedPlayerController + Offsets.ObservedPlayerController.HandsController;
            ObservedHealthController = Memory.ReadPtr(ObservedPlayerController + Offsets.ObservedPlayerController.HealthController);
            ArgumentOutOfRangeException.ThrowIfNotEqual(this,
                Memory.ReadValue<ulong>(ObservedHealthController + Offsets.ObservedHealthController._player),
                nameof(ObservedHealthController));
            CorpseAddr = ObservedHealthController + Offsets.ObservedHealthController._playerCorpse;

            MovementContext = GetMovementContext();
            RotationAddress = ValidateRotationAddr(MovementContext + Offsets.ObservedPlayerStateContext.Rotation);
            /// Setup Transform
            var ti = Memory.ReadPtrChain(this, false, _transformInternalChain);
            SkeletonRoot = new UnityTransform(ti);
            _ = SkeletonRoot.UpdatePosition();

            bool isAI = Memory.ReadValue<bool>(this + Offsets.ObservedPlayerView.IsAI);
            IsHuman = !isAI;
            Id = GetPlayerId();
            /// Determine Player Type
            PlayerSide = (Enums.EPlayerSide)Memory.ReadValue<int>(this + Offsets.ObservedPlayerView.Side); // Usec,Bear,Scav,etc.
            if (!Enum.IsDefined(PlayerSide)) // Make sure PlayerSide is valid
                throw new ArgumentOutOfRangeException(nameof(PlayerSide));
            if (IsScav)
            {
                if (isAI)
                {
                    if (_raidCache.SpecialAi.TryGetValue(Id, out var specialRole))
                    {
                        Name = specialRole.Name;
                        Type = specialRole.Type;
                    }
                    else
                    {
                        var voicePtr = Memory.ReadPtr(this + Offsets.ObservedPlayerView.Voice);
                        string voice = Memory.ReadUnityString(voicePtr);
                        _isUsecBearAi = IsUsecBearVoice(voice);
                        _usecBearAiFactionName = GetUsecBearFactionName(voice);
                        var role = GetInitialAIRole(voice);
                        Name = role.Name;
                        Type = role.Type;
                    }
                }
                else
                {
                    Name = $"PScav{Id}";
                    Type = PlayerType.PScav;
                }
            }
            else if (IsPmc)
            {
                Name = $"{PlayerSide}{Id}";
                Type = PlayerType.PMC;
            }
            else
                throw new NotImplementedException(nameof(PlayerSide));
            Equipment = new PlayerEquipment(this);
            // Map native group string to internal group id the same way LocalPlayer/ClientPlayer does
            try
            {
                SetGroupId(GetGroupNumber());
            }
            catch { GroupId = TryGetGroup(Id); }
            if (GroupId == TeammateGroupId)
            {
                Type = PlayerType.Teammate;
            }
            IsFocused = _raidCache is RaidCache rc && rc.Focused.ContainsKey(Id);
        }

        private string ReadGroupString()
        {
            try
            {
                var profilePtr = Memory.ReadPtr(this + Offsets.Player.Profile);
                if (profilePtr == 0) return string.Empty;
                var infoPtr = Memory.ReadPtr(profilePtr + Offsets.Profile.Info);
                if (infoPtr == 0) return string.Empty;
                return Memory.ReadUnityString(infoPtr + Offsets.PlayerInfo.GroupId);
            }
            catch { return string.Empty; }
        }

        private int GetGroupNumber()
        {
            try
            {
                string grp = ReadGroupString();
                if (string.IsNullOrEmpty(grp))
                    return TryGetGroup(Id);

                // If matches local player's group string -> teammate
                var local = Memory.LocalPlayer as LocalPlayer;
                if (local is not null)
                {
                    try
                    {
                        var localGroup = Memory.ReadUnityString(local.Profile + Offsets.PlayerInfo.GroupId);
                        if (!string.IsNullOrEmpty(localGroup) && localGroup == grp)
                            return TeammateGroupId;
                    }
                    catch { }
                }

                // Generate a stable positive id from the group string using FNV-1a 32-bit
                uint hash = 2166136261u;
                foreach (var b in System.Text.Encoding.UTF8.GetBytes(grp))
                {
                    hash ^= b;
                    hash *= 16777619u;
                }
                // Ensure id fits in int and is positive, and not equal to reserved ids
                int gid = (int)(hash & 0x7FFFFFFF);
                if (gid == SoloGroupId || gid == TeammateGroupId || gid == 0)
                {
                    gid = Math.Max(1, Math.Abs(gid) + 1);
                }

                // Persist mapping for this player so other code can reapply it
                try
                {
                    if (Config.Cache.RaidCache is RaidCache rc)
                    {
                        rc.Groups[Id] = gid;
                    }
                }
                catch { }

                return gid;
            }
            catch
            {
                return TryGetGroup(Id);
            }
        }

        private void SetGroupId(int gid)
        {
            GroupId = gid;
            try
            {
                if (Config.Cache.RaidCache is RaidCache rc)
                {
                    if (gid == SoloGroupId)
                        _ = rc.Groups.TryRemove(Id, out _);
                    else
                        rc.Groups[Id] = gid;
                }
            }
            catch { }
        }

        /// <summary>
        /// If the Player is Human, Toggle Teammate Status.
        /// </summary>
        public void ToggleTeammate()
        {
            bool isTeammate = Type == PlayerType.Teammate;
            if (!IsHuman)
                return;
            AssignTeammate(!isTeammate);
        }

        /// <summary>
        /// Assign this Player to a Group.
        /// </summary>
        /// <param name="groupId"></param>
        public void AssignGroup(int groupId)
        {
            try
            {
                // Prevent changing groups after raid has been locked (>30 seconds after start)
                if ((_gameWorld is not null && _gameWorld.RaidStartedAt.HasValue &&
                    DateTime.UtcNow - _gameWorld.RaidStartedAt.Value > TimeSpan.FromSeconds(30)) ||
                    (Config.Cache.RaidCache?.GroupsLocked == true))
                {
                    // If already in requested group, allow (no-op). Otherwise ignore.
                    if (GroupId == groupId)
                        return;
                    Logging.WriteLine($"[ObservedPlayer] AssignGroup ignored for player {Id} - groups locked.");
                    return;
                }
            }
            catch { }
            GroupId = groupId;
        }

        /// <summary>
        /// Assign this Player as a Teammate to <see cref="LocalPlayer"/>.
        /// </summary>
        /// <param name="isTeammate">True if the Player is a Teammate, otherwise false.</param>
        public void AssignTeammate(bool isTeammate)
        {
            try
            {
                if ((_gameWorld is not null && _gameWorld.RaidStartedAt.HasValue &&
                    DateTime.UtcNow - _gameWorld.RaidStartedAt.Value > TimeSpan.FromSeconds(30)) ||
                    (Config.Cache.RaidCache?.GroupsLocked == true))
                {
                    // If already in the desired teammate state, allow; otherwise ignore changes.
                    if (isTeammate && Type == PlayerType.Teammate)
                        return;
                    if (!isTeammate && (Type == PlayerType.PMC || Type == PlayerType.PScav))
                        return;
                    Logging.WriteLine($"[ObservedPlayer] AssignTeammate ignored for player {Id} - groups locked.");
                    return;
                }
            }
            catch { }

            if (isTeammate)
            {
                Type = PlayerType.Teammate;
                GroupId = TeammateGroupId;
            }
            else
            {
                Type = PlayerSide == Enums.EPlayerSide.Savage ? PlayerType.PScav : PlayerType.PMC;
                GroupId = SoloGroupId;
            }
        }

        /// <summary>
        /// Focus or Unfocus this Player.
        /// </summary>
        /// <param name="isFocused">True to focus the player, otherwise false.</param>
        public override void SetFocus(bool isFocused)
        {
            IsFocused = isFocused;
            if (isFocused)
            {
                _raidCache.Focused.TryAdd(Id, 0);
            }
            else
            {
                _ = _raidCache.Focused.TryRemove(Id, out _);
            }
        }

        /// <summary>
        /// Get the Player's ID.
        /// </summary>
        /// <returns>Player Id.</returns>
        private int GetPlayerId()
        {
            return Memory.ReadValueEnsure<int>(this + Offsets.ObservedPlayerView.Id);
        }

        /// <summary>
        /// Tries to get an existing Group Id (if possible).
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        private int TryGetGroup(int id)
        {
            if (Config.Misc.AutoGroups && IsHuman && IsPmc &&
                _raidCache is RaidCache raidCache &&
                raidCache.Groups.TryGetValue(id, out var group))
            {
                return group;
            }
            return SoloGroupId;
        }

        /// <summary>
        /// Get Movement Context Instance.
        /// </summary>
        private ulong GetMovementContext()
        {
            var movementController = Memory.ReadPtrChain(ObservedPlayerController, true, Offsets.ObservedPlayerController.MovementController, Offsets.ObservedPlayerMovementController.ObservedPlayerStateContext);
            return movementController;
        }

        /// <summary>
        /// Sync Player Information.
        /// </summary>
        public override void OnRegRefresh(VmmScatter scatter, ISet<ulong> registered, bool? isActiveParam = null)
        {
            if (isActiveParam is not bool isActive)
                isActive = registered.Contains(this);
            if (isActive)
            {
                UpdateHealthStatus();
            }
            base.OnRegRefresh(scatter, registered, isActive);

            // Periodically attempt to refresh human group information from the observed player's profile view
            try
            {
                // Run every ~1.5 seconds
                if (_lastGroupRefresh == default || DateTime.UtcNow - _lastGroupRefresh > TimeSpan.FromSeconds(1.5))
                {
                    _lastGroupRefresh = DateTime.UtcNow;
                    TryRefreshHumanGroupFromView();
                }
            }
            catch { }

            TryFinalizeSpecialAiPostInit();
        }

        private void TryFinalizeSpecialAiPostInit()
        {
            if (_specialAiPostInitFinalized || !IsAI)
                return;

            if (Equipment is null || !Equipment.IsSnapshotCaptured)
                return;

            try
            {
                if (GetSpecialAiRole() is AIRole role)
                {
                    AssignSpecialAiRole(role);
                }

                // Snapshot is immutable after initialization; one pass is sufficient.
                _specialAiPostInitFinalized = true;
            }
            catch
            {
                // Keep pending so we can retry on next refresh if anything transient failed.
            }
        }

        /// <summary>
        /// Get Player's Updated Health Condition
        /// Only works in Online Mode.
        /// </summary>
        private void UpdateHealthStatus()
        {
            try
            {
                var tag = (Enums.ETagStatus)Memory.ReadValue<int>(ObservedHealthController + Offsets.ObservedHealthController.HealthStatus);
                if ((tag & Enums.ETagStatus.Dying) == Enums.ETagStatus.Dying)
                    HealthStatus = Enums.ETagStatus.Dying;
                else if ((tag & Enums.ETagStatus.BadlyInjured) == Enums.ETagStatus.BadlyInjured)
                    HealthStatus = Enums.ETagStatus.BadlyInjured;
                else if ((tag & Enums.ETagStatus.Injured) == Enums.ETagStatus.Injured)
                    HealthStatus = Enums.ETagStatus.Injured;
                else
                    HealthStatus = Enums.ETagStatus.Healthy;
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"ERROR updating Health Status for '{Name}': {ex}");
            }
        }

        private static readonly uint[] _transformInternalChain =
        [
            Offsets.ObservedPlayerView.PlayerBody,
            Offsets.PlayerBody.SkeletonRootJoint,
            Offsets.DizSkinningSkeleton._values,
            UnityList<byte>.ArrOffset,
            UnityList<byte>.ArrStartOffset + (uint)Bones.HumanBase * 0x8,
            0x10
        ];

        #region AI Player Roles

        private static readonly FrozenDictionary<string, AIRole> _aiRolesByVoice = new Dictionary<string, AIRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["BossSanitar"] = new() { Name = "Sanitar", Type = PlayerType.AIBoss },
            ["BossBully"] = new() { Name = "Reshala", Type = PlayerType.AIBoss },
            ["BossGluhar"] = new() { Name = "Gluhar", Type = PlayerType.AIBoss },
            ["SectantPriest"] = new() { Name = "Priest", Type = PlayerType.AIBoss },
            ["SectantWarrior"] = new() { Name = "Cultist", Type = PlayerType.AIRaider },
            ["BossKilla"] = new() { Name = "Killa", Type = PlayerType.AIBoss },
            ["BossTagilla"] = new() { Name = "Tagilla", Type = PlayerType.AIBoss },
            ["Boss_Partizan"] = new() { Name = "Partisan", Type = PlayerType.AIBoss },
            ["BossBigPipe"] = new() { Name = "Big Pipe", Type = PlayerType.AIBoss },
            ["BossBirdEye"] = new() { Name = "Birdeye", Type = PlayerType.AIBoss },
            ["BossKnight"] = new() { Name = "Knight", Type = PlayerType.AIBoss },
            ["Arena_Guard_1"] = new() { Name = "Arena Guard", Type = PlayerType.AIScav },
            ["Arena_Guard_2"] = new() { Name = "Arena Guard", Type = PlayerType.AIScav },
            ["Boss_Kaban"] = new() { Name = "Kaban", Type = PlayerType.AIBoss },
            ["Boss_Kollontay"] = new() { Name = "Kollontay", Type = PlayerType.AIBoss },
            ["Boss_Sturman"] = new() { Name = "Shturman", Type = PlayerType.AIBoss },
            ["Zombie_Generic"] = new() { Name = "Zombie", Type = PlayerType.AIScav },
            ["BossZombieTagilla"] = new() { Name = "Zombie Tagilla", Type = PlayerType.AIBoss },
            ["Zombie_Fast"] = new() { Name = "Zombie", Type = PlayerType.AIScav },
            ["Zombie_Medium"] = new() { Name = "Zombie", Type = PlayerType.AIScav },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> _shturmanGuardWeapons = new HashSet<string>
        {
            "tokarevsvt40762x54rrifle",
            "svds762x54rsniperrifle",
            "molotarmsvpo101veperhunter762x51carbine",
            "sr25",
            "springfieldarmorym1a762x51rifle",
            "mosin762x54rboltactionriflesniper",
            "opsks",
            "vssvintorez9x39specialsniperrifle",
            "sr3m9x39compactassaultrifle",
            "keltecrfb762x51rifle",
        }.ToFrozenSet();

        private static readonly FrozenSet<string> _shturmanGuardSupportGear = new HashSet<string>
        {
            "wilcoxskulllockheadmount",
            "6b43zabraloshbodyarmoremr",
            "bntizhukbodyarmoremr",
            "fortredutmbodyarmor",
            "6b232bodyarmormountainflora",
            "6b231armor",
            "bntikirasanbodyarmor",
            "anatacticalalphachestrigolivedrab",
            "dynaforcetritonm43achestharnessblack",
            "umtbs6sh112scoutsniperchestrigemr",
            "splavtarzanm22chestrigsmog",
            "vkboarmybag",
            "scavbackpack",
            "wartechberkutbb102backpackatacsfg",
            "hazard4takedownslingbackpackblack",
            "hazard4takedownslingbackpackmulticam",
        }.ToFrozenSet();

        private static readonly FrozenSet<string> _sanitarGuardWeapons = new HashSet<string>
        {
            "molotarmsvpo101veperhunter762x51carbine",
            "saiga12kver1012gasemiautomaticshotgun",
            "molotarmsvpo136veprkm762x39carbine",
            "kalashnikovakm762x39assaultrifle",
            "kalashnikovaks74ub545x39assaultrifle",
        }.ToFrozenSet();

        private static readonly FrozenSet<string> _sanitarGuardSupportGear = new HashSet<string>
        {
            "emercomcap",
            "kindacowboyhat",
            "sssh94sferashelmet",
            "altyn",
            "bntilshz2dtmhelmet",
            "highcomstrikerachhciiiahelmet",
            "aybolitmask",
            "momexbalaclava",
            "smokebalaclava",
            "pestilyplaguemask",
            "6b43zabraloshbodyarmor",
            "lbt6094aslickplatecarrier",
            "fortdefender2bodyarmor",
            "fortredutmbodyarmor",
            "bntigzhelkbodyarmor",
            "nppkiasskorundvmbodyarmor",
            "bntikirasanbodyarmor",
            "dynaforcetritonm43achestharness",
            "wartechtv109tv106chestrig",
            "splavtarzanm22chestrig",
            "ssoattack2raidbackpack",
            "anatacticalbeta2battlebackpack",
            "lbt2670slimfieldmedpack",
            "wartechberkutbb102backpack",
            "dufflebag",
        }.ToFrozenSet();

        private static readonly FrozenSet<string> _itemVariantTokens = new HashSet<string>
        {
            "black",
            "olive",
            "drab",
            "khaki",
            "tan",
            "coyote",
            "multicam",
            "smog",
            "atacs",
            "fg",
            "flora",
            "emr",
        }.ToFrozenSet();

        /// <summary>
        /// Get Initial AI Role for this player.
        /// Checks voice line against known roles, otherwise falls back to pattern matching, and other misc. checks.
        /// </summary>
        /// <returns><see cref="AIRole"/> for this player.</returns>
        private AIRole GetInitialAIRole(string voiceLine)
        {
            if (!_aiRolesByVoice.TryGetValue(voiceLine, out AIRole role))
            {
                // Fallback pattern matching
                role = voiceLine switch
                {
                    _ when voiceLine.Contains("scav", StringComparison.OrdinalIgnoreCase) => new() { Name = "Scav", Type = PlayerType.AIScav },
                    _ when voiceLine.Contains("boss", StringComparison.OrdinalIgnoreCase) => new() { Name = "Boss", Type = PlayerType.AIBoss },
                    _ when voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase) => GetDefaultUsecBearAiRole(),
                    _ when voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase) => GetDefaultUsecBearAiRole(),
                    _ when voiceLine.Contains("black_division", StringComparison.OrdinalIgnoreCase) => new() { Name = "BD", Type = PlayerType.AIRaider },
                    _ when voiceLine.Contains("vsrf", StringComparison.OrdinalIgnoreCase) => new() { Name = "Vsrf", Type = PlayerType.AIRaider },
                    _ when voiceLine.Contains("civilian", StringComparison.OrdinalIgnoreCase) => new() { Name = "Civ", Type = PlayerType.AIScav },
                    _ => new() { Name = "AI", Type = PlayerType.AIScav }
                };
            }

            // Known USEC/BEAR AI voices should default to Raider/Rogue until dogtag confirms AIPMC.
            if (IsUsecBearVoice(voiceLine))
            {
                return GetDefaultUsecBearAiRole();
            }

            // Labs Raider Check
            if (_gameWorld.MapID == "laboratory" && role.Type != PlayerType.AIBoss)
            {
                role = new("Raider", PlayerType.AIRaider);
            }
            return role;
        }

        private AIRole GetDefaultUsecBearAiRole()
        {
            return _gameWorld.MapID == "lighthouse"
                ? new("Rogue", PlayerType.AIRaider)
                : new("Raider", PlayerType.AIRaider);
        }

        private static bool IsUsecBearVoice(string voiceLine)
        {
            return voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase)
                || voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetUsecBearFactionName(string voiceLine)
        {
            if (voiceLine.Contains("usec", StringComparison.OrdinalIgnoreCase))
                return "Usec";
            if (voiceLine.Contains("bear", StringComparison.OrdinalIgnoreCase))
                return "Bear";
            return null;
        }

        /// <summary>
        /// Assign a Special AI Role for this player.
        /// Usually done during pre-raid checks for Santa/Guards/etc.
        /// </summary>
        /// <param name="name">New special role to be set. Set to <see langword="null"/> to revert to original role.</param>
        public void AssignSpecialAiRole(AIRole? role)
        {
            if (role is AIRole value)
            {
                _specialName = value.Name;
                _specialType = value.Type;
                _raidCache.SpecialAi.TryAdd(Id, value);
            }
            else
            {
                _specialName = null;
                _specialType = null;
                _ = _raidCache.SpecialAi.TryRemove(Id, out _);
            }
        }

        /// <summary>
        /// Check's if this AI Unit is a "special" role based on special checks.
        /// </summary>
        /// <returns><see cref="AIRole"/> value if a special unit, otherwise <see langword="null"/>.</returns>
        public AIRole? GetSpecialAiRole()
        {
            if (!IsAI || Equipment?.Items?.Values is not IEnumerable<TarkovMarketItem> items)
                return null;

            // For AI PMC-like units, use dogtag faction when available and keep voice as fallback.
            // If no faction can be confirmed, default to Raider/Rogue.
            if (Equipment.IsSnapshotCaptured)
            {
                var dogtagFaction = Equipment.DogtagFaction;
                if (!string.IsNullOrEmpty(dogtagFaction))
                {
                    return new(dogtagFaction, PlayerType.AIRaider);
                }

                if (Equipment.HasDogtag)
                {
                    // Dogtag exists but faction cannot be resolved from dogtag nor voice fallback.
                    // Classify as generic PMC instead of Rogue/Raider.
                    if (!_isUsecBearAi || string.IsNullOrEmpty(_usecBearAiFactionName))
                    {
                        return new("PMC", PlayerType.PMC);
                    }
                }

                if (_isUsecBearAi)
                {
                    var factionName = string.IsNullOrEmpty(_usecBearAiFactionName) ? "AIPMC" : _usecBearAiFactionName;
                    if (!string.Equals(factionName, "AIPMC", StringComparison.OrdinalIgnoreCase) && Equipment.HasDogtag)
                    {
                        return new(factionName, PlayerType.AIRaider);
                    }

                    return GetDefaultUsecBearAiRole();
                }
            }

            if (items.Any(i => i.BsgId == "61b9e1aaef9a1b5d6a79899a")) // Santa's Bag
            {
                return new("Santa", PlayerType.AIBoss);
            }
            else if (items.Any(i => i.BsgId == "63626d904aa74b8fe30ab426")) // Zryachiy's balaclava
            {
                return new("Zryachiy", PlayerType.AIBoss);
            }
            return null;
        }

        #endregion
    }

    public readonly record struct AIRole(string Name, PlayerType Type);

}

