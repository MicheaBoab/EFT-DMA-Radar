/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using Collections.Pooled;
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Tarkov.World.Player;
using System.Globalization;
using LoneEftDmaRadar;

namespace LoneEftDmaRadar.Tarkov.World
{
    public sealed class RegisteredPlayers : IReadOnlyCollection<AbstractPlayer>
    {
        #region Fields/Properties/Constructor

        public static implicit operator ulong(RegisteredPlayers x) => x.Base;
        private ulong Base { get; }
        private readonly GameWorld _game;
        private readonly ConcurrentDictionary<ulong, AbstractPlayer> _players = new();

        /// <summary>
        /// LocalPlayer Instance.
        /// </summary>
        public LocalPlayer LocalPlayer { get; }

        /// <summary>
        /// RegisteredPlayers List Constructor.
        /// </summary>
        public RegisteredPlayers(ulong baseAddr, GameWorld game)
        {
            Base = baseAddr;
            _game = game;
            var mainPlayer = Memory.ReadPtr(_game + Offsets.GameWorld.MainPlayer, false);
            var localPlayer = new LocalPlayer(mainPlayer);
            _players[localPlayer] = LocalPlayer = localPlayer;
        }

        #endregion

        /// <summary>
        /// Updates the ConcurrentDictionary of 'Players'
        /// </summary>
        public void Refresh()
        {
            try
            {
                using var playersList = UnityList<ulong>.Create(this, false); // Realtime Read
                using var registered = playersList.Where(x => x != 0x0).ToPooledSet();
                /// Allocate New Players
                bool anyNew = false;
                foreach (var playerBase in registered)
                {
                    if (playerBase == LocalPlayer) // Skip LocalPlayer, already allocated
                        continue;
                    // Add new player if missing
                    if (!_players.ContainsKey(playerBase))
                    {
                        AbstractPlayer.Allocate(_players, playerBase, _game);
                        anyNew = true;
                        try
                        {
                            // If allocated and is an ObservedPlayer, record its spawn position
                            if (_players.TryGetValue(playerBase, out var p) && p is ObservedPlayer op)
                            {
                                var pos = op.Position;
                                string posStr = string.Format(CultureInfo.InvariantCulture, "{0};{1};{2}", pos.X, pos.Y, pos.Z);
                                Program.Config.Cache.RaidCache.SpawnPositions[op.Id] = posStr;
                            }
                        }
                        catch { }
                    }
                }
                // Trigger auto-grouping only if we allocated new players (spawn/reconnect)
                if (anyNew)
                {
                    try
                    {
                        _game.UpdateAutoGroups(allowForming: true);
                    }
                    catch (Exception ex)
                    {
                        Logging.WriteLine($"[RegisteredPlayers] UpdateAutoGroups ERROR: {ex}");
                    }
                }
                /// Update Existing Players incl LocalPlayer
                UpdateExistingPlayers(registered);
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"CRITICAL ERROR - RegisteredPlayers Loop FAILED: {ex}");
            }
        }

        /// <summary>
        /// Returns the Player Count currently in the Registered Players List.
        /// </summary>
        /// <returns>Count of players.</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public int GetPlayerCount()
        {
            var count = Memory.ReadValue<int>(this + UnityList<byte>.CountOffset, false);
            if (count < 0 || count > 256)
                throw new ArgumentOutOfRangeException(nameof(count));
            return count;
        }

        /// <summary>
        /// Scans the existing player list and updates Players as needed.
        /// </summary>
        private void UpdateExistingPlayers(ISet<ulong> registered)
        {
            var allPlayers = _players.Values;
            if (allPlayers.Count == 0)
                return;
            using var scatter = Memory.CreateScatter(VmmSharpEx.Options.VmmFlags.NOCACHE);
            scatter.PrepareRead(this, 8); // Add a single read to prevent failures
            foreach (var player in allPlayers)
            {
                player.OnRegRefresh(scatter, registered);
            }
            scatter.Execute();
        }

        /// <summary>
        /// Checks if there is an existing BTR player in the Players Dictionary, and if not, it is allocated and swapped.
        /// </summary>
        /// <param name="btrPlayerBase">Player Base Addr for BTR Operator.</param>
        public void TryAllocateBTR(ulong btrView, ulong btrPlayerBase)
        {
            if (_players.TryGetValue(btrPlayerBase, out var existing) && existing is not BtrPlayer)
            {
                var btr = new BtrPlayer(btrView, btrPlayerBase, _game);
                _players[btrPlayerBase] = btr;
                Logging.WriteLine("BTR Allocated!");
            }
        }

        #region IReadOnlyCollection
        public int Count => _players.Values.Count;
        public IEnumerator<AbstractPlayer> GetEnumerator() =>
            _players.Values.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        #endregion
    }
}

