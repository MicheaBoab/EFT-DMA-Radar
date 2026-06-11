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
    public sealed partial class WebRadarTransit
    {
        [MemoryPackOrder(0)] public string Name { get; set; }
        [MemoryPackOrder(1)] public float X { get; set; }
        [MemoryPackOrder(2)] public float Y { get; set; }
        [MemoryPackOrder(3)] public float Z { get; set; }
        [MemoryPackOrder(4)] public float Height { get; set; }

        public static WebRadarTransit Create(TransitPoint transit, EftMapConfig map)
        {
            var pos = transit.Position;
            var mapPos = transit.Position.ToMapPos(map);
            return new WebRadarTransit
            {
                Name = transit.Description,
                X = mapPos.X,
                Y = mapPos.Y,
                Z = pos.Z,
                Height = pos.Y
            };
        }
    }
}
