/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Misc.JSON;
using LoneEftDmaRadar.Web.TarkovDev;
using System.Collections.Frozen;

namespace LoneEftDmaRadar.Tarkov
{
    /// <summary>
    /// Manages Tarkov Dynamic Data (TarkovDevItems, Quests, etc).
    /// </summary>
    public static class TarkovDataManager
    {
        private const string DATA_FILE = "data.json";
        private const string DATA_FILE_PVE = "data-pve.json";

        /// <summary>
        /// Master items dictionary - mapped via BSGID String.
        /// </summary>
        public static FrozenDictionary<string, TarkovMarketItem> AllItems { get; private set; }

        /// <summary>
        /// Master containers dictionary - mapped via BSGID String.
        /// </summary>
        public static FrozenDictionary<string, TarkovMarketItem> AllContainers { get; private set; }
        /// <summary>
        /// Maps Data for Tarkov.
        /// </summary>
        public static FrozenDictionary<string, TarkovDevTypes.MapElement> MapData { get; private set; }
        /// <summary>
        ///  Tasks Data for Tarkov.
        /// </summary>
        public static FrozenDictionary<string, TarkovDevTypes.TaskElement> TaskData { get; private set; }
        /// <summary>
        /// All Task Zones mapped by MapID -> ZoneID -> Position.
        /// </summary>
        public static FrozenDictionary<string, FrozenDictionary<string, Vector3>> TaskZones { get; private set; }
        /// <summary>
        /// Event fired when data is updated. Reference the <see cref="TarkovDataManager"/> static properties for updated data.
        /// </summary>
        public static event EventHandler DataUpdated;
        private static void OnDataUpdated()
        {
            DataUpdated?.Invoke(null, EventArgs.Empty);
        }

        #region Startup

        /// <summary>
        /// Call to start EftDataManager Module. ONLY CALL ONCE.
        /// </summary>
        /// <param name="loading">Loading UI Form.</param>
        /// <param name="defaultOnly">True if you want to load cached/default query only.</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task ModuleInitAsync(bool defaultOnly = false)
        {
            try
            {
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"ERROR loading Game/Loot Data ({GetDataFiles().Data.Name})", ex);
            }
        }

        /// <summary>
        /// Reloads the currently selected Tarkov.dev data mode from cache/default data and refreshes it from the web.
        /// </summary>
        public static async Task ReloadAsync()
        {
            var files = GetDataFiles();
            if (files.Data.Exists)
                await LoadDiskDataAsync();
            else
                await LoadDefaultDataAsync();

            await LoadRemoteDataAsync();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Loads Game/FilteredLoot Data and sets the static dictionaries.
        /// If updated query is needed, spawns a background task to retrieve it.
        /// </summary>
        /// <returns></returns>
        private static async Task LoadDataAsync()
        {
            var files = GetDataFiles();
            if (files.Data.Exists)
            {
                DateTime lastWriteTime = File.GetLastWriteTime(files.Data.FullName);
                await LoadDiskDataAsync();
                if (lastWriteTime < DateTime.Now.Subtract(TimeSpan.FromHours(4))) // only update every 4h
                {
                    _ = Task.Run(LoadRemoteDataAsync); // Run continuations on the thread pool.
                }
            }
            else
            {
                await LoadDefaultDataAsync();
                _ = Task.Run(LoadRemoteDataAsync); // Run continuations on the thread pool.
            }
        }

        /// <summary>
        /// Sets the input <paramref name="data"/> into the static dictionaries.
        /// </summary>
        /// <param name="data">Data to be set.</param>
        private static void SetData(TarkovDevTypes.DataElement data)
        {
            AllItems = data.Items.Where(x => !x.Tags?.Contains("Static Container") ?? false)
                .DistinctBy(x => x.BsgId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k.BsgId, v => v, StringComparer.OrdinalIgnoreCase)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            AllContainers = data.Items.Where(x => x.Tags?.Contains("Static Container") ?? false)
                .DistinctBy(x => x.BsgId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(k => k.BsgId, v => v, StringComparer.OrdinalIgnoreCase)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            TaskData = (data.Tasks ?? new List<TarkovDevTypes.TaskElement>())
                .Where(t => !string.IsNullOrWhiteSpace(t?.Id))
                .DistinctBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            TaskZones = TaskData.Values
                .Where(task => task.Objectives is not null) // Ensure the Objectives are not null
                .SelectMany(task => task.Objectives)   // Flatten the Objectives from each TaskElement
                .Where(objective => objective.Zones is not null) // Ensure the Zones are not null
                .SelectMany(objective => objective.Zones)    // Flatten the Zones from each Objective
                .Where(zone => zone.Position != default && zone.Map?.NameId is not null) // Ensure Position and Map are not null
                .GroupBy(zone => zone.Map.NameId, zone => new
                {
                    id = zone.Id,
                    pos = new Vector3(zone.Position.X, zone.Position.Y, zone.Position.Z)
                }, StringComparer.OrdinalIgnoreCase)
                .DistinctBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key, // Map Id
                    group => group
                    .DistinctBy(x => x.id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        zone => zone.id,
                        zone => zone.pos,
                        StringComparer.OrdinalIgnoreCase
                    ).ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase
                )
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            var maps = data.Maps.ToDictionary(x => x.NameId, StringComparer.OrdinalIgnoreCase) ??
                new Dictionary<string, TarkovDevTypes.MapElement>(StringComparer.OrdinalIgnoreCase);
            MapData = maps.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
            OnDataUpdated();
        }

        /// <summary>
        /// Loads default embedded <see cref="TarkovData"/> and sets the static dictionaries.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private static async Task LoadDefaultDataAsync()
        {
            const string resource = "LoneEftDmaRadar.Resources.DEFAULT_DATA.json";
            using var dataStream = Utilities.OpenResource(resource);
            var data = await JsonSerializer.DeserializeAsync(dataStream, AppJsonContext.Default.DataElement)
                ?? throw new InvalidOperationException($"Failed to deserialize {nameof(dataStream)}");
            SetData(data);
        }

        /// <summary>
        /// Loads <see cref="TarkovData"/> from disk and sets the static dictionaries.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static async Task LoadDiskDataAsync()
        {
            var files = GetDataFiles();
            var data = await TryLoadFromDiskAsync(files.Temp) ??
                await TryLoadFromDiskAsync(files.Data) ??
                await TryLoadFromDiskAsync(files.Backup);
            if (data is null) // Internal soft failover
            {
                files.Data.Delete();
                await LoadDefaultDataAsync();
                return;
            }
            SetData(data);

            static async Task<TarkovDevTypes.DataElement> TryLoadFromDiskAsync(FileInfo file)
            {
                try
                {
                    if (!file.Exists)
                        return null;
                    using var dataStream = File.OpenRead(file.FullName);
                    return await JsonSerializer.DeserializeAsync(dataStream, AppJsonContext.Default.DataElement) ??
                        throw new InvalidOperationException($"Failed to deserialize {nameof(dataStream)}");
                }
                catch
                {
                    return null; // Ignore errors, return null to indicate failure
                }
            }
        }

        /// <summary>
        /// Loads updated Game/FilteredLoot Data from the web and sets the static dictionaries.
        /// </summary>
        /// <returns></returns>
        /// <exception cref="InvalidOperationException"></exception>
        private static async Task LoadRemoteDataAsync()
        {
            try
            {
                var files = GetDataFiles();
                var data = await TarkovDevGraphQLApi.GetTarkovDataAsync();
                ArgumentNullException.ThrowIfNull(data, nameof(data));
                var dataJson = JsonSerializer.Serialize(data, AppJsonContext.Default.DataElement);
                await File.WriteAllTextAsync(files.Temp.FullName, dataJson);
                if (files.Data.Exists)
                {
                    File.Replace(
                        sourceFileName: files.Temp.FullName,
                        destinationFileName: files.Data.FullName,
                        destinationBackupFileName: files.Backup.FullName,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Copy(
                        sourceFileName: files.Temp.FullName,
                        destFileName: files.Backup.FullName,
                        overwrite: true);
                    File.Move(
                        sourceFileName: files.Temp.FullName,
                        destFileName: files.Data.FullName,
                        overwrite: true);
                }
                SetData(data);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    messageBoxText: $"An unhandled exception occurred while retrieving updated Game/Loot Data from the web: {ex}",
                    caption: Program.Name,
                    button: MessageBoxButton.OK,
                    icon: MessageBoxImage.Warning,
                    options: MessageBoxOptions.DefaultDesktopOnly);
            }
        }

        private static (FileInfo Data, FileInfo Temp, FileInfo Backup) GetDataFiles()
        {
            string dataFileName = Program.Config.Loot.UsePvEData ? DATA_FILE_PVE : DATA_FILE;
            var dataFile = new FileInfo(Path.Combine(Program.ConfigPath.FullName, dataFileName));
            var tempDataFile = new FileInfo(Path.Combine(Program.ConfigPath.FullName, dataFileName + ".tmp"));
            var bakDataFile = new FileInfo(Path.Combine(Program.ConfigPath.FullName, dataFileName + ".bak"));
            return (dataFile, tempDataFile, bakDataFile);
        }

        #endregion
    }
}
