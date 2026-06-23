/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.Unity;

namespace LoneEftDmaRadar.Tarkov.World.Exits
{
    /// <summary>
    /// Runtime exfiltration point information read from game memory.
    /// Combines configuration data (name, position) with real-time status.
    /// </summary>
    public class RuntimeExfilInfo
    {
        /// <summary>
        /// Memory address of the ExfiltrationPoint object.
        /// </summary>
        public ulong Address { get; set; }

        /// <summary>
        /// Runtime status of the exfil (0-7).
        /// 0 = NotPresent, 1 = UncompleteRequirements, 2 = Countdown (✓ available),
        /// 3 = RegularMode (✓ available), 4 = Pending (✗ not available),
        /// 5 = AwaitsManualActivation (✗ needs action), 6 = Hidden, 7 = Unknown (secret only)
        /// </summary>
        public byte Status { get; set; }

        /// <summary>
        /// World position of the exfil from Transform.
        /// </summary>
        public Vector3 RuntimePosition { get; set; }

        /// <summary>
        /// Settings name from ExitTriggerSettings.
        /// Used as primary key for matching with config exfils.
        /// Example: "EXFIL_Train", "Alpinist", "Factory Gate"
        /// </summary>
        public string SettingsName { get; set; }

        /// <summary>
        /// Settings ID (if available).
        /// </summary>
        public string SettingsId { get; set; }

        /// <summary>
        /// Exfil type: 0 = standard, 1 = special (train, vehicle, secret, etc).
        /// </summary>
        public int SettingsType { get; set; }

        /// <summary>
        /// Whether the event/activation is available for this exfil.
        /// Relevant for special exfils (trains, vehicles, etc).
        /// </summary>
        public bool EventAvailable { get; set; }

        /// <summary>
        /// Whether this exfil can be used multiple times in the raid.
        /// </summary>
        public bool Reusable { get; set; }

        /// <summary>
        /// Whether scavengers can use this exfil.
        /// Only relevant for ScavExfiltrationPoints.
        /// </summary>
        public bool ScavEligible { get; set; }

        /// <summary>
        /// Whether PMCs can use this exfil.
        /// Only relevant for ScavExfiltrationPoints (always true for regular points).
        /// </summary>
        public bool PmcEligible { get; set; }

        /// <summary>
        /// Distance between config position and runtime position.
        /// Used for validation. High distance may indicate version mismatch.
        /// </summary>
        public float PositionDistance { get; set; }

        /// <summary>
        /// Whether the status indicates exfil is currently available.
        /// </summary>
        public bool IsAvailable => Status == 2 || Status == 3;

        /// <summary>
        /// Whether the status requires manual activation.
        /// </summary>
        public bool RequiresManualActivation => Status == 5;

        public override string ToString()
        {
            return $"{SettingsName} (status={Status}, avail={IsAvailable}, pos_dist={PositionDistance:F1}m)";
        }
    }
}
