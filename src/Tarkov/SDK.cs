namespace SDK
{
    public readonly partial struct Offsets
    {
        public readonly partial struct GameWorld        // ETF.GameWorld
        {
            public const uint BtrController = 0x28;
            public const uint LocationId = 0xE8;
            public const uint LootList = 0x1B0;
            public const uint RegisteredPlayers = 0x1D0;
            public const uint MainPlayer = 0x230;
            public const uint SynchronizableObjectLogicProcessor = 0x270;
            public const uint Grenades = 0x2B0;
        }

        public readonly partial struct SynchronizableObject     // ETF.SynchronizableObjects.SynchronizableObject
        {
            public const uint Type = 0x68;
        }

        public readonly partial struct SynchronizableObjectLogicProcessor       // ETF.SynchronizableObjects.SynchronizableObjectLogicProcessor
        {
            public const uint _staticSynchronizableObjects = 0x18;
        }

        public readonly partial struct TripwireSynchronizableObject     // EFT.SynchronizableObjects.TripwireSynchronizableObject
        {
            public const uint _tripwireState = 0xE4;
            public const uint ToPosition = 0x158;
        }

        public readonly partial struct BtrController        // EFT.Vehicle.BtrController
        {
            public const uint BtrView = 0x50;
        }

        public readonly partial struct BTRView      // EFT.Vehicle.BTRView
        {
            public const uint turret = 0x60;
            public const uint _previousPosition = 0xB4;
        }

        public readonly partial struct BTRTurretView        // EFT.Vehicle.BTRTurretView
        {
            public const uint _bot = 0x60;
        }

        public readonly partial struct Throwable            // Throwable : UnityEngine.MonoBehaviour
        {
            public const uint _isDestroyed = 0x4D;
        }

        public readonly partial struct Player       // EFT.Player : UnityEngine.MonoBehaviour
        {
            public const uint MovementContext = 0x60;
            public const uint _playerBody = 0x190;
            public const uint GameWorld = 0x680;
            public const uint Corpse = 0x718;
            public const uint Location = 0x918;
            public const uint Profile = 0x9B0;
            public const uint _handsController = 0xA30;
            public const uint _playerLookRaycastTransform = 0xAC0;
        }

        public readonly partial struct ObservedPlayerView       // EFT.NextObservedPlayer.ObservedPlayerView
        {
            public const uint ObservedPlayerController = 0x28;
            public const uint Voice = 0x40;
            public const uint Id = 0x7C;
            public const uint Side = 0x94;
            public const uint IsAI = 0xA0;
            public const uint PlayerBody = 0xD8;
        }

        public readonly partial struct ObservedPlayerController       // EFT.NextObservedPlayer.ObservedPlayerController 
        {
            public const uint InventoryController = 0x10;
            public const uint PlayerView = 0x18;
            public const uint MovementController = 0xD8;
            public const uint HealthController = 0xE8;
            public const uint HandsController = 0x120;
        }

        public readonly partial struct ObservedPlayerHandsController       // EFT.NextObservedPlayer.ObservedPlayerHandsController
        {
            public const uint _item = 0x58;
        }

        public readonly partial struct InventoryController       // EFT.InventoryLogic.InventoryController
        {
            public const uint Inventory = 0x100;
        }

        public readonly partial struct Inventory        // EFT.InventoryLogic.Inventory
        {
            public const uint Equipment = 0x18;
        }

        public readonly partial struct InventoryEquipment       // EFT.InventoryLogic.InventoryEquipment
        {
            public const uint _cachedSlots = 0x90;
        }

        public readonly partial struct Slot             // EFT.InventoryLogic.Slot
        {
            public const uint ContainedItem = 0x48;
            public const uint ID = 0x58;
        }

        public readonly partial struct ObservedPlayerMovementController         // EFT.NextObservedPlayer.ObservedPlayerMovementController
        {
            public const uint ObservedPlayerStateContext = 0x98;
        }

        public readonly partial struct ObservedPlayerStateContext       // EFT.NextObservedPlayer.ObservedPlayerStateContext
        {
            public const uint Rotation = 0x28;
        }

        public readonly partial struct ObservedHealthController         // ObservedPlayerHealthController : System.Object
        {
            public const uint HealthRate = 0x134;
            public const uint HealthStatus = 0x10;
            public const uint _player = 0x18;
            public const uint _playerCorpse = 0x20;
        }

        public readonly partial struct Profile                  // EFT.Profile
        {
            public const uint Id = 0x10;
            public const uint AccountId = 0x18;
            public const uint Info = 0x48;
            public const uint QuestsData = 0x98;
            public const uint WishlistManager = 0x108;
        }

        public readonly partial struct WishlistManager       // EFT.WishlistManager
        {
            public const uint _wishlistItems = 0x30;
        }

        public readonly partial struct PlayerInfo         // EFT.ProfileInfo
        {
            public const uint Side = 0x48;
            public const uint RegistrationDate = 0x4C;
            public const uint GroupId = 0x50;
        }

        public readonly partial struct QuestsData           // EFT.Quests.QuestStatusData
        {
            public const uint Id = 0x10;
            public const uint Status = 0x1C;
            public const uint CompletedConditions = 0x28;
        }

        public readonly partial struct MovementContext          // EFT.MovementContext
        {
            public const uint _player = 0x40;
            public const uint _rotation = 0xC0;
        }

        public readonly partial struct InteractiveLootItem          // EFT.Interactive.LootItem
        {
            public const uint _item = 0xF0;
        }

        public readonly partial struct DizSkinningSkeleton          // Diz.Skinning.Skeleton 
        {
            public const uint _values = 0x30;
        }

        public readonly partial struct LootableContainer            //  EFT.Interactive.LootableContainer
        {
            public const uint ItemOwner = 0x178;
        }

        public readonly partial struct ItemController         // EFT.InventoryLogic.ItemController
        {
            public const uint RootItem = 0xD0;
        }

        public readonly partial struct LootItem          // EFT.InventoryLogic.Item
        {
            public const uint Template = 0x60;
        }

        public readonly partial struct ItemTemplate         // EFT.InventoryLogic.ItemTemplate 
        {
            public const uint ShortName = 0x18;
            public const uint QuestItem = 0x34;
            public const uint _id = 0xE0;
        }

        public readonly partial struct PlayerBody           // EFT.PlayerBody
        {
            public const uint SkeletonRootJoint = 0x30;
        }

        public readonly partial struct GamePlayerOwner           // EFT.GamePlayerOwner
        {
            public const uint _myPlayer = 0x08;
        }
    }

    public readonly partial struct Enums
    {
        public enum EPlayerSide     // EFT.EPlayerSide 
        {
            Usec = 1,
            Bear = 2,
            Savage = 4
        }

        [Flags]
        public enum ETagStatus
        {
            Unaware = 1,
            Aware = 2,
            Combat = 4,
            Solo = 8,
            Coop = 16,
            Bear = 32,
            Usec = 64,
            Scav = 128,
            TargetSolo = 256,
            TargetMultiple = 512,
            Healthy = 1024,
            Injured = 2048,
            BadlyInjured = 4096,
            Dying = 8192,
            Birdeye = 16384,
            Knight = 32768,
            BigPipe = 65536,
            BlackDivision = 131072,
            VSRF = 262144
        }

        [Flags]
        public enum EMemberCategory
        {
            Default = 0,
            Developer = 1,
            UniqueId = 2,
            Trader = 4,
            Group = 8,
            System = 16,
            ChatModerator = 32,
            ChatModeratorWithPermanentBan = 64,
            UnitTest = 128,
            Sherpa = 256,
            Emissary = 512,
            Unheard = 1024
        }

        public enum SynchronizableObjectType
        {
            AirDrop = 0,
            AirPlane = 1,
            Tripwire = 2
        }

        public enum ETripwireState
        {
            None = 0,
            Wait = 1,
            Active = 2,
            Exploding = 3,
            Exploded = 4,
            Inert = 5
        }
    }
}