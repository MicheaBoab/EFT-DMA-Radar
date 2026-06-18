/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.World.Loot;
using LoneEftDmaRadar.UI.Maps;
using LoneEftDmaRadar.UI.Skia;
using MemoryPack;

namespace LoneEftDmaRadar.Web.WebRadar.Data
{
    [MemoryPackable]
    public sealed partial class WebRadarLoot
    {
        [MemoryPackOrder(0)] public string ShortName { get; set; }
        [MemoryPackOrder(1)] public int Price { get; set; }
        [MemoryPackOrder(2)] public float X { get; set; }
        [MemoryPackOrder(3)] public float Y { get; set; }
        [MemoryPackOrder(4)] public float Z { get; set; }
        [MemoryPackOrder(5)] public bool IsMeds { get; set; }
        [MemoryPackOrder(6)] public bool IsFood { get; set; }
        [MemoryPackOrder(7)] public bool IsBackpack { get; set; }
        [MemoryPackOrder(8)] public string BsgId { get; set; }
        [MemoryPackOrder(9)] public string ColorHex { get; set; }

        public static WebRadarLoot Create(LootItem loot, EftMapConfig map)
        {
            var pos = loot.Position;
            var mapPos = loot.Position.ToMapPos(map);
            return new WebRadarLoot
            {
                ShortName = loot.ShortName,
                Price = loot.Price,
                X = mapPos.X,
                Y = mapPos.Y,
                Z = pos.Z,
                IsMeds = loot.IsMeds,
                IsFood = loot.IsFood,
                IsBackpack = loot.IsBackpack,
                BsgId = loot.ID,
                ColorHex = ResolveLootColorHex(loot)
            };
        }

        private static string ResolveLootColorHex(LootItem loot)
        {
            if (!string.IsNullOrWhiteSpace(loot.CustomFilterColor) && SkiaSharp.SKColor.TryParse(loot.CustomFilterColor, out var customColor))
                return ToCssHex(customColor);

            return ToCssHex(SKPaints.PaintImportantLoot.Color);
        }

        private static string ToCssHex(SkiaSharp.SKColor color)
        {
            return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }
    }
}
