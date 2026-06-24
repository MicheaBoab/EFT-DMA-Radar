/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.World.Player;
using LoneEftDmaRadar.Tarkov.World.Player.Helpers;
using LoneEftDmaRadar.UI.Maps;
using LoneEftDmaRadar.UI.Skia;
using MemoryPack;

namespace LoneEftDmaRadar.Web.WebRadar.Data
{
    [MemoryPackable]
    public partial struct WebRadarPlayer
    {
        /// <summary>
        /// Player Name.
        /// </summary>
        [MemoryPackOrder(0)]
        public string Name { get; set; }
        /// <summary>
        /// Player Type (PMC, Scav,etc.)
        /// </summary>
        [MemoryPackOrder(1)]
        public WebPlayerType Type { get; set; }
        /// <summary>
        /// True if player is active, otherwise False.
        /// </summary>
        [MemoryPackOrder(2)]
        public bool IsActive { get; set; }
        /// <summary>
        /// True if player is alive, otherwise False.
        /// </summary>
        [MemoryPackOrder(3)]
        public bool IsAlive { get; set; }
        /// <summary>
        /// Unity World Position.
        /// </summary>
        [MemoryPackOrder(4)]
        public Vector3 Position { get; set; }
        /// <summary>
        /// Unity World Rotation.
        /// </summary>
        [MemoryPackOrder(5)]
        public Vector2 Rotation { get; set; }
        /// <summary>
        /// True when this entity is the local player.
        /// </summary>
        [MemoryPackOrder(6)]
        public bool IsLocal { get; set; }
        /// <summary>
        /// True when this entity is friendly to the local player.
        /// </summary>
        [MemoryPackOrder(7)]
        public bool IsFriendly { get; set; }
        /// <summary>
        /// Yaw in degrees.
        /// </summary>
        [MemoryPackOrder(8)]
        public float Yaw { get; set; }
        /// <summary>
        /// String type used by web client renderer.
        /// </summary>
        [MemoryPackOrder(9)]
        public string TypeName { get; set; }
        /// <summary>
        /// True when unit is AI.
        /// </summary>
        [MemoryPackOrder(10)]
        public bool IsAI { get; set; }
        /// <summary>
        /// True when unit is PMC.
        /// </summary>
        [MemoryPackOrder(11)]
        public bool IsPmc { get; set; }

        /// <summary>
        /// True if map-space coordinates were computed server-side.
        /// </summary>
        [MemoryPackOrder(12)]
        public bool HasMapPos { get; set; }

        /// <summary>
        /// Map-space X coordinate computed using desktop ToMapPos rules.
        /// </summary>
        [MemoryPackOrder(13)]
        public float MapX { get; set; }

        /// <summary>
        /// Map-space Y coordinate computed using desktop ToMapPos rules.
        /// </summary>
        [MemoryPackOrder(14)]
        public float MapY { get; set; }

        /// <summary>
        /// True when unit is AI boss.
        /// </summary>
        [MemoryPackOrder(15)]
        public bool IsBoss { get; set; }

        /// <summary>
        /// Unity world-space height (Y) copied explicitly for web label math.
        /// </summary>
        [MemoryPackOrder(16)]
        public float HeightY { get; set; }

        /// <summary>
        /// True when unit is raider/rogue/guard category.
        /// </summary>
        [MemoryPackOrder(17)]
        public bool IsRaider { get; set; }

        /// <summary>
        /// True when AIRaider actually maps to AI PMC (Usec/Bear AI).
        /// </summary>
        [MemoryPackOrder(18)]
        public bool IsAIPmc { get; set; }

        /// <summary>
        /// Final player color resolved from desktop radar paint logic.
        /// </summary>
        [MemoryPackOrder(19)]
        public string ColorHex { get; set; }

        /// <summary>
        /// Create a WebRadarPlayer from a Full Player Object.
        /// </summary>
        /// <param name="player">Full EFT Player Object.</param>
        /// <returns>Compact WebRadarPlayer object.</returns>
        public static WebRadarPlayer Create(AbstractPlayer player)
        {
            WebPlayerType type = player is LocalPlayer ?
                WebPlayerType.LocalPlayer : player.IsFriendly ?
                WebPlayerType.Teammate : player.IsHuman ?
                player.IsScav ?
                WebPlayerType.PlayerScav : WebPlayerType.Player : WebPlayerType.Bot;
            return new WebRadarPlayer
            {
                Name = player.Name,
                Type = type,
                IsActive = player.IsActive,
                IsAlive = player.IsAlive,
                Position = player.Position,
                Rotation = player.Rotation,
                IsLocal = player is LocalPlayer,
                IsFriendly = player.IsFriendly,
                Yaw = player.Rotation.X,
                TypeName = type.ToString(),
                IsAI = !player.IsHuman,
                IsPmc = player.IsHuman && !player.IsScav,
                HasMapPos = false,
                MapX = 0,
                MapY = 0,
                IsBoss = player.Type == PlayerType.AIBoss,
                HeightY = player.Position.Y,
                IsRaider = player.Type == PlayerType.AIRaider,
                IsAIPmc = player is ObservedPlayer obs
                    && player.Type == PlayerType.AIRaider
                    && !string.IsNullOrEmpty(obs.UsecBearAiFactionName)
                    && (string.Equals(player.Name, "AIPMC", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(player.Name, "Usec", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(player.Name, "Bear", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(player.Name, obs.UsecBearAiFactionName, StringComparison.OrdinalIgnoreCase)),
                ColorHex = GetPlayerColorHex(player)
            };
        }

        private static string GetPlayerColorHex(AbstractPlayer player)
        {
            if (player.IsFocused)
                return ToCssHex(SKPaints.PaintFocused.Color);
            if (player is LocalPlayer)
                return ToCssHex(SKPaints.PaintLocalPlayer.Color);

            return player.Type switch
            {
                PlayerType.Teammate => ToCssHex(SKPaints.PaintTeammate.Color),
                PlayerType.PMC => ToCssHex(SKPaints.PaintPMC.Color),
                PlayerType.AIScav => ToCssHex(SKPaints.PaintScav.Color),
                PlayerType.AIRaider => player is ObservedPlayer obs
                    && !string.IsNullOrEmpty(obs.UsecBearAiFactionName)
                    && (string.Equals(player.Name, "AIPMC", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(player.Name, "Usec", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(player.Name, "Bear", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(player.Name, obs.UsecBearAiFactionName, StringComparison.OrdinalIgnoreCase))
                        ? ToCssHex(SKPaints.PaintAIPMC.Color)
                        : ToCssHex(SKPaints.PaintRaider.Color),
                PlayerType.AIBoss => ToCssHex(SKPaints.PaintBoss.Color),
                PlayerType.PScav => ToCssHex(SKPaints.PaintPScav.Color),
                _ => ToCssHex(SKPaints.PaintPMC.Color)
            };
        }

        private static string ToCssHex(SkiaSharp.SKColor color)
        {
            return $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
        }

        /// <summary>
        /// Create a WebRadarPlayer from a Full Player Object with server-side map projection.
        /// </summary>
        /// <param name="player">Full EFT Player Object.</param>
        /// <param name="map">Current map configuration.</param>
        /// <returns>Compact WebRadarPlayer object with map coordinates when available.</returns>
        public static WebRadarPlayer Create(AbstractPlayer player, EftMapConfig map)
        {
            var projected = Create(player);
            try
            {
                var mapPos = player.Position.ToMapPos(map);
                projected.HasMapPos = true;
                projected.MapX = mapPos.X;
                projected.MapY = mapPos.Y;
            }
            catch
            {
                projected.HasMapPos = false;
            }

            return projected;
        }
    }
}

