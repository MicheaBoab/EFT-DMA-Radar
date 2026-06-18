/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.UI.Maps;

namespace LoneEftDmaRadar.Web.WebRadar.Data
{
    internal static class WebRadarMapConverter
    {
        public static WebRadarMapInfo Convert(string id, EftMapConfig cfg)
        {
            return new WebRadarMapInfo
            {
                Id = id,
                X = cfg.X,
                Y = cfg.Y,
                Scale = cfg.Scale,
                SvgScale = cfg.RasterScale,
                DisableDimming = cfg.DisableDimming,
                Layers = cfg.MapLayers?.Select(l => new WebRadarMapLayer
                {
                    MinHeight = l.MinHeight,
                    MaxHeight = l.MaxHeight,
                    CannotDimLowerLayers = l.CannotDimLowerLayers,
                    Filename = l.Filename
                }).ToList() ?? new List<WebRadarMapLayer>()
            };
        }
    }
}
