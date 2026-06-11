/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using MemoryPack;

namespace LoneEftDmaRadar.Web.WebRadar.Data
{
    [MemoryPackable]
    public sealed partial class WebRadarMapInfo
    {
        [MemoryPackOrder(0)] public string Id { get; set; }
        [MemoryPackOrder(1)] public float X { get; set; }
        [MemoryPackOrder(2)] public float Y { get; set; }
        [MemoryPackOrder(3)] public float Scale { get; set; }
        [MemoryPackOrder(4)] public float SvgScale { get; set; }
        [MemoryPackOrder(5)] public bool DisableDimming { get; set; }
        [MemoryPackOrder(6)] public List<WebRadarMapLayer> Layers { get; set; } = new();
    }

    [MemoryPackable]
    public sealed partial class WebRadarMapLayer
    {
        [MemoryPackOrder(0)] public float? MinHeight { get; set; }
        [MemoryPackOrder(1)] public float? MaxHeight { get; set; }
        [MemoryPackOrder(2)] public bool CannotDimLowerLayers { get; set; }
        [MemoryPackOrder(3)] public string Filename { get; set; }
    }
}
