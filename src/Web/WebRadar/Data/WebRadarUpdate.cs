/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using MemoryPack;

namespace LoneEftDmaRadar.Web.WebRadar.Data
{
    [MemoryPackable]
    public sealed partial class WebRadarUpdate
    {
        /// <summary>
        /// Update version (used for ordering).
        /// </summary>
        [MemoryPackOrder(0)]
        public ulong Version { get; set; } = 0;
        /// <summary>
        /// True if In-Game, otherwise False.
        /// </summary>
        [MemoryPackOrder(1)]
        public bool InGame { get; set; } = false;
        /// <summary>
        /// Alias used by the web client.
        /// </summary>
        [MemoryPackOrder(2)]
        public bool InRaid { get; set; } = false;
        /// <summary>
        /// Contains the Map ID of the current map.
        /// </summary>
        //[JsonIgnore]
        [MemoryPackOrder(3)]
        public string MapID { get; set; } = null;
        /// <summary>
        /// Lower-cased mapId field expected by the web client.
        /// </summary>
        [MemoryPackOrder(4)]
        public string WebMapId { get; set; } = null;
        /// <summary>
        /// All Players currently on the map.
        /// </summary>
        [MemoryPackOrder(5)]
        public WebRadarPlayer[] Players { get; set; } = null;
        /// <summary>
        /// Map conversion metadata used by the browser renderer.
        /// </summary>
        [MemoryPackOrder(6)]
        public WebRadarMapInfo Map { get; set; } = null;
        /// <summary>
        /// Live loot entities for web radar overlays.
        /// </summary>
        [MemoryPackOrder(7)]
        public WebRadarLoot[] Loot { get; set; } = null;
        /// <summary>
        /// Live extracts for web radar overlays.
        /// </summary>
        [MemoryPackOrder(8)]
        public WebRadarExfil[] Exfils { get; set; } = null;
        /// <summary>
        /// Live transits for web radar overlays.
        /// </summary>
        [MemoryPackOrder(9)]
        public WebRadarTransit[] Transits { get; set; } = null;
        /// <summary>
        /// UTC server send timestamp.
        /// </summary>
        [MemoryPackOrder(10)]
        public DateTime SendTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Base aimline length in pixels for local/friendly units.
        /// </summary>
        [MemoryPackOrder(11)]
        public int AimLineLength { get; set; } = 1500;

        /// <summary>
        /// Maximum range used by high-alert aimline checks.
        /// </summary>
        [MemoryPackOrder(12)]
        public float MaxDistance { get; set; } = 350f;

        /// <summary>
        /// True when teammate aimlines are enabled.
        /// </summary>
        [MemoryPackOrder(13)]
        public bool TeammateAimlines { get; set; } = false;

        /// <summary>
        /// True when AI aimlines are enabled.
        /// </summary>
        [MemoryPackOrder(14)]
        public bool AIAimlines { get; set; } = true;

        /// <summary>
        /// Death marker color resolved from desktop radar paint.
        /// </summary>
        [MemoryPackOrder(15)]
        public string DeathMarkerColorHex { get; set; } = "#000000";

        /// <summary>
        /// Corpse label/color resolved from desktop radar paint.
        /// </summary>
        [MemoryPackOrder(16)]
        public string CorpseColorHex { get; set; } = "#C0C0C0";
    }
}

