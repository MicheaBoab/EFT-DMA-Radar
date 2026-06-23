/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.Unity;
using LoneEftDmaRadar.Tarkov.World.Player;
using LoneEftDmaRadar.UI.Maps;
using LoneEftDmaRadar.UI.Skia;
using LoneEftDmaRadar.Web.TarkovDev;

namespace LoneEftDmaRadar.Tarkov.World.Exits
{
    public class Exfil : IExitPoint, IWorldEntity, IMapEntity, IMouseoverEntity
    {
        private readonly TarkovDevTypes.ExtractElement _configData;
        private readonly Vector3 _position;
        private GameWorld _gameWorld;
        private RuntimeExfilInfo _runtimeInfo;

        public Exfil(TarkovDevTypes.ExtractElement extract, GameWorld gameWorld = null)
        {
            Name = extract.Name;
            _configData = extract;
            _position = extract.Position;
            _gameWorld = gameWorld;
            _runtimeInfo = null;
        }

        public string Name { get; }

        /// <summary>
        /// Sets the GameWorld reference for runtime data queries.
        /// </summary>
        internal void SetGameWorld(GameWorld gameWorld)
        {
            _gameWorld = gameWorld;
        }

        #region Interfaces

        public ref readonly Vector3 Position => ref _position;
        public Vector2 MouseoverPosition { get; set; }
        
        /// <summary>
        /// Gets the runtime exfiltration info if available (for web sync).
        /// </summary>
        public RuntimeExfilInfo RuntimeInfo
        {
            get
            {
                // Try to get runtime info if not cached yet
                if (_gameWorld != null && _runtimeInfo == null)
                {
                    _gameWorld.TryGetRuntimeExfilInfo(Name, _position, out _runtimeInfo);
                }
                return _runtimeInfo;
            }
        }

        public void Draw(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            // Try to get runtime info if we have a gameworld reference
            var configPos = _configData.Position;
            if (_gameWorld != null && _runtimeInfo == null)
            {
                _gameWorld.TryGetRuntimeExfilInfo(Name, configPos, out _runtimeInfo);
            }

            var heightDiff = configPos.Y - localPlayer.Position.Y;
            
            // Determine paint color based on runtime status
            var paint = GetExfilPaint(_runtimeInfo);
            
            var point = configPos.ToMapPos(mapParams.Map).ToZoomedPos(mapParams);
            MouseoverPosition = new Vector2(point.X, point.Y);
            SKPaints.ShapeOutline.StrokeWidth = 2f;

            if (heightDiff > 1.85f) // exfil is above player
            {
                using var path = point.GetUpArrow(6.5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else if (heightDiff < -1.85f) // exfil is below player
            {
                using var path = point.GetDownArrow(6.5f);
                canvas.DrawPath(path, SKPaints.ShapeOutline);
                canvas.DrawPath(path, paint);
            }
            else // exfil is level with player
            {
                const float size = 4.75f;
                canvas.DrawCircle(point, size, SKPaints.ShapeOutline);
                canvas.DrawCircle(point, size, paint);
            }

            string exfilName = string.IsNullOrWhiteSpace(Name) ? "unknown" : Name;
            var textPoint = point;
            textPoint.Offset(9.5f, 0);
            canvas.DrawText(exfilName, textPoint, SKTextAlign.Left, SKFonts.UIRegular, SKPaints.TextOutline);
            canvas.DrawText(exfilName, textPoint, SKTextAlign.Left, SKFonts.UIRegular, paint);
        }

        public void DrawMouseover(SKCanvas canvas, EftMapParams mapParams, LocalPlayer localPlayer)
        {
            var exfilName = Name;
            exfilName ??= "unknown";
            
            // Append runtime status info if available
            if (_runtimeInfo != null)
            {
                exfilName = $"{exfilName} [{GetStatusDisplayName(_runtimeInfo.Status)}]";
            }
            
            var configPos = _configData.Position;
            configPos.ToMapPos(mapParams.Map).ToZoomedPos(mapParams).DrawMouseoverText(canvas, exfilName);
        }

        #endregion

        #region Runtime Status Handling

        /// <summary>
        /// Determines the paint color based on runtime exfil status.
        /// Status priority: Available (green) > ManualActivation (orange) > Pending (red) > Unknown (default)
        /// </summary>
        private SKPaint GetExfilPaint(RuntimeExfilInfo info)
        {
            if (info == null)
            {
                // No runtime data available - use default
                return SKPaints.PaintExfil;
            }

            return info.Status switch
            {
                // Status 2: Countdown, Status 3: RegularMode - both mean available for extraction
                2 or 3 => SKPaints.PaintExfilAvailable ?? SKPaints.PaintExfil,

                // Status 5: AwaitsManualActivation - needs player action (button, extract, etc)
                5 => SKPaints.PaintExfilManualActivation ?? SKPaints.PaintExfil,

                // Status 4: Pending - not available yet
                4 => SKPaints.PaintExfilPending ?? SKPaints.PaintExfil,

                // Status 0,1,6: Not present / incomplete requirements / hidden
                0 or 1 or 6 => SKPaints.PaintExfilUnavailable ?? SKPaints.PaintExfil,

                // Status 7 or any unknown - use default
                _ => SKPaints.PaintExfil
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

        #endregion
    }
}

