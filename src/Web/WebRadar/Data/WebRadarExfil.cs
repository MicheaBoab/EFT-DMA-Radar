/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.World.Exits;
using LoneEftDmaRadar.UI.Maps;
using LoneEftDmaRadar.UI.Skia;
using MemoryPack;

namespace LoneEftDmaRadar.Web.WebRadar.Data
{
    [MemoryPackable]
    public sealed partial class WebRadarExfil
    {
        [MemoryPackOrder(0)] public string Name { get; set; }
        [MemoryPackOrder(1)] public float X { get; set; }
        [MemoryPackOrder(2)] public float Y { get; set; }
        [MemoryPackOrder(3)] public float Z { get; set; }
        [MemoryPackOrder(4)] public bool IsAvailableForPlayer { get; set; }
        [MemoryPackOrder(5)] public bool IsSecret { get; set; }
        [MemoryPackOrder(6)] public float Height { get; set; }
        /// <summary>Runtime exfil status (0-7): 0=NotPresent, 2=Available, 4=Pending, 5=NeedsActivation, 7=Secret</summary>
        [MemoryPackOrder(7)] public byte RuntimeStatus { get; set; } = 255; // 255 = no runtime data
        /// <summary>Human-readable status name from runtime</summary>
        [MemoryPackOrder(8)] public string StatusName { get; set; }

        public static WebRadarExfil Create(Exfil exfil, EftMapConfig map)
        {
            var pos = exfil.Position;
            var mapPos = exfil.Position.ToMapPos(map);
            
            // Get runtime status if available
            var runtimeInfo = exfil.RuntimeInfo;
            var statusName = "Unknown";
            var isAvailable = true; // Default: available
            byte runtimeStatus = 255; // Default: no data
            
            if (runtimeInfo != null)
            {
                runtimeStatus = runtimeInfo.Status;
                statusName = GetStatusDisplayName(runtimeStatus);
                isAvailable = runtimeInfo.IsAvailable; // Status 2,3 = available
            }
            
            return new WebRadarExfil
            {
                Name = exfil.Name,
                X = mapPos.X,
                Y = mapPos.Y,
                Z = pos.Z,
                Height = pos.Y,
                IsAvailableForPlayer = isAvailable,
                IsSecret = runtimeStatus == 7, // Status 7 = secret
                RuntimeStatus = runtimeStatus,
                StatusName = statusName
            };
        }
        
        /// <summary>
        /// Gets a human-readable display name for exfil status.
        /// </summary>
        private static string GetStatusDisplayName(byte status)
        {
            return status switch
            {
                0 => "NotPresent",
                1 => "IncompleteRequirements",
                2 => "Available",
                3 => "Available",
                4 => "Pending",
                5 => "NeedsActivation",
                6 => "Hidden",
                7 => "Secret",
                _ => "Unknown"
            };
        }
    }
}
