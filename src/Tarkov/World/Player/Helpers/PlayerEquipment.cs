/*
 * Lone EFT DMA Radar - Copyright (c) 2026 Lone DMA
 * Licensed under GNU AGPLv3. See https://www.gnu.org/licenses/agpl-3.0.html
 */
using LoneEftDmaRadar.Tarkov.Unity.Collections;
using LoneEftDmaRadar.Web.TarkovDev;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace LoneEftDmaRadar.Tarkov.World.Player.Helpers
{
    public sealed class PlayerEquipment
    {
        private const string SECURED_CONTAINER_SLOT = "SecuredContainer";
        private const string DOGTAG_SLOT = "Dogtag";
        private static readonly FrozenSet<string> _skipSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Compass", "ArmBand", "Eyewear", "Pockets"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        private static readonly FrozenSet<string> _usecDogtagIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "59f32c3b86f77472a31742f0",
            "6662e9f37fa79a6d83730fa0",
            "6662ea05f6259762c56f3189",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        private static readonly FrozenSet<string> _bearDogtagIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "59f32bb586f774757e1e8442",
            "6662e9aca7e0b43baa3d5f74",
            "6662e9cda7e0b43baa3d5f76",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ulong> _slots = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, TarkovMarketItem> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly ObservedPlayer _player;
        private bool _inited;
        private bool _snapshotCaptured;
        private int _cachedValue;
        private ulong _hands;

        /// <summary>
        /// Player's eqiuipped gear by slot.
        /// </summary>
        public IReadOnlyDictionary<string, TarkovMarketItem> Items => _items;
        /// <summary>
        /// Player's secured container item.
        /// </summary>
        [MaybeNull]
        public TarkovMarketItem SecuredContainer
        {
            get
            {
                _ = _items.TryGetValue(SECURED_CONTAINER_SLOT, out var item);
                return item;
            }
        }
        /// <summary>
        /// Player's item in hands.
        /// </summary>
        [MaybeNull]
        public TarkovMarketItem InHands { get; private set; }
        /// <summary>
        /// Player's total equipment flea price value.
        /// </summary>
        public int Value => _cachedValue;
        /// <summary>
        /// True if the player is carrying any important loot items.
        /// </summary>
        public bool CarryingImportantLoot => _items?.Values?.Any(item => item.IsImportant) ?? false;

        /// <summary>
        /// True when the player's equipment includes a dogtag slot item.
        /// </summary>
        public bool HasDogtag => _items?.ContainsKey(DOGTAG_SLOT) ?? false;

        /// <summary>
        /// Dogtag faction derived from dogtag item metadata when available.
        /// </summary>
        [MaybeNull]
        public string DogtagFaction
        {
            get
            {
                if (!_items.TryGetValue(DOGTAG_SLOT, out var dogtag) || dogtag is null)
                    return null;

                if (_usecDogtagIds.Contains(dogtag.BsgId) || ContainsFactionToken(dogtag, "USEC"))
                    return "Usec";

                if (_bearDogtagIds.Contains(dogtag.BsgId) || ContainsFactionToken(dogtag, "BEAR"))
                    return "Bear";

                return null;
            }
        }

        /// <summary>
        /// True when initial one-time equipment snapshot has been captured.
        /// </summary>
        public bool IsSnapshotCaptured => _inited;

        public PlayerEquipment(ObservedPlayer player)
        {
            _player = player;
            _ = Task.Run(InitAsnyc); // Lazy init
        }

        private async Task InitAsnyc()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var inventorycontroller = Memory.ReadPtr(_player.InventoryControllerAddr);
                    var inventory = Memory.ReadPtr(inventorycontroller + Offsets.InventoryController.Inventory);
                    var equipment = Memory.ReadPtr(inventory + Offsets.Inventory.Equipment);
                    var slotsPtr = Memory.ReadPtr(equipment + Offsets.InventoryEquipment._cachedSlots);
                    using var slotsArray = UnityArray<ulong>.Create(slotsPtr, true);
                    ArgumentOutOfRangeException.ThrowIfLessThan(slotsArray.Count, 1);

                    foreach (var slotPtr in slotsArray)
                    {
                        var namePtr = Memory.ReadPtr(slotPtr + Offsets.Slot.ID);
                        var name = Memory.ReadUnityString(namePtr);
                        if (_skipSlots.Contains(name))
                            continue;
                        _slots.TryAdd(name, slotPtr);
                    }

                    Refresh(checkInit: false);
                    _inited = true;
                    _snapshotCaptured = true;
                    return;
                }
                catch (Exception ex)
                {
                    Logging.WriteLine($"Error initializing Player Equipment for '{_player.Name}': {ex}");
                }
                finally
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
        }

        public void Refresh(bool checkInit = true)
        {
            // Equipment is intended to be a one-time initialization snapshot.
            if (_snapshotCaptured)
                return;
            GetEquipment(checkInit);
            GetHands();
        }

        private void GetEquipment(bool checkInit = true)
        {
            try
            {
                if (checkInit && !_inited)
                    return;
                long totalValue = 0;
                foreach (var slot in _slots)
                {
                    try
                    {
                        if (_player.IsPmc && slot.Key == "Scabbard")
                            continue;

                        var containedItem = Memory.ReadPtr(slot.Value + Offsets.Slot.ContainedItem);
                        var inventorytemplate = Memory.ReadPtr(containedItem + Offsets.LootItem.Template);
                        var mongoId = Memory.ReadValue<MongoID>(inventorytemplate + Offsets.ItemTemplate._id);
                        var id = mongoId.ReadString();
                        if (TarkovDataManager.AllItems.TryGetValue(id, out var item))
                        {
                            _items[slot.Key] = item;
                            totalValue += item.FleaPrice;
                        }
                        else
                        {
                            _items.TryRemove(slot.Key, out _);
                        }
                    }
                    catch
                    {
                        _items.TryRemove(slot.Key, out _);
                    }
                }
                _cachedValue = (int)totalValue;
            }
            catch (Exception ex)
            {
                Logging.WriteLine($"Error refreshing Player Equipment for '{_player.Name}': {ex}");
            }
        }

        private void GetHands()
        {
            if (!_player.IsHuman) // Don't care about non-human players' hands
                return;
            try
            {
                var handsController = Memory.ReadPtr(_player.HandsControllerAddr); // or FirearmController
                var itemBase = Memory.ReadPtr(handsController + Offsets.ObservedPlayerHandsController._item);
                if (itemBase != _hands)
                {
                    InHands = null;
                    var itemTemplate = Memory.ReadPtr(itemBase + Offsets.LootItem.Template);
                    var itemMongoId = Memory.ReadValue<MongoID>(itemTemplate + Offsets.ItemTemplate._id);
                    var itemID = itemMongoId.ReadString();
                    if (TarkovDataManager.AllItems.TryGetValue(itemID, out var heldItem)) // Item exists in DB
                    {
                        InHands = heldItem;
                    }
                    else // Item doesn't exist in DB , use name from game memory
                    {
                        var itemNamePtr = Memory.ReadPtr(itemTemplate + Offsets.ItemTemplate.ShortName);
                        var itemName = Memory.ReadUnityString(itemNamePtr)?.Trim();
                        if (string.IsNullOrEmpty(itemName))
                            itemName = "Item";
                        InHands = new()
                        {
                            Name = itemName,
                            ShortName = itemName
                        };
                    }
                    _hands = itemBase;
                }
            }
            catch (Exception ex)
            {
                InHands = null;
                _hands = default;
                Logging.WriteLine($"Error refreshing Player Hands for '{_player.Name}': {ex}");
            }
        }

        private static bool ContainsFactionToken(TarkovMarketItem item, string token)
        {
            return item.Name?.Contains(token, StringComparison.OrdinalIgnoreCase) == true ||
                item.ShortName?.Contains(token, StringComparison.OrdinalIgnoreCase) == true;
        }
    }
}

