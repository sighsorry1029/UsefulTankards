using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace UsefulTankards;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class UsefulTankardsPlugin : BaseUnityPlugin
{
    public const string ModName = "UsefulTankards";
    public const string ModVersion = "0.1.0";
    public const string Author = "sighsorry";
    public const string ModGuid = Author + "." + ModName;

    internal static ManualLogSource Log = null!;

    private static readonly ConfigSync ConfigSync = new(ModGuid)
    {
        DisplayName = ModName,
        CurrentVersion = ModVersion,
        MinimumRequiredVersion = ModVersion
    };

    internal enum Toggle
    {
        On = 1,
        Off = 0
    }

    private static ConfigEntry<Toggle> ServerConfigLocked = null!;
    internal static ConfigEntry<Toggle> EnableMod = null!;
    internal static ConfigEntry<float> MovementWhileDrinking = null!;
    internal static ConfigEntry<Toggle> TankardStorage = null!;
    internal static ConfigEntry<Toggle> DrinkStoredMeadsOnUse = null!;
    internal static ConfigEntry<int> MaxTankardsInInventory = null!;

    internal static bool ModEnabled => EnableMod.Value == Toggle.On;
    internal static float MovementWhileDrinkingMultiplier => Math.Min(1f, Math.Max(0f, MovementWhileDrinking.Value));
    internal static bool TankardStorageEnabled => TankardStorage.Value == Toggle.On;
    internal static bool DrinkStoredMeadsOnUseEnabled => DrinkStoredMeadsOnUse.Value == Toggle.On;

    private readonly Harmony _harmony = new(ModGuid);

    private void Awake()
    {
        Log = Logger;

        ServerConfigLocked = ConfigEntry(
            "1 - General",
            "Lock Configuration",
            Toggle.On,
            "If on, the configuration is locked and can be changed by server admins only.",
            order: 1000);
        _ = ConfigSync.AddLockingConfigEntry(ServerConfigLocked);

        EnableMod = ConfigEntry(
            "1 - General",
            "Enable",
            Toggle.On,
            "Enable UsefulTankards tweaks.",
            order: 900);
        MovementWhileDrinking = ConfigEntry(
            "1 - General",
            "Movement While Drinking",
            1f,
            new ConfigDescription(
                "Movement and rotation speed multiplier while drinking through a tankard. 0 keeps vanilla movement lock; 1 allows normal movement.",
                new AcceptableValueRange<float>(0f, 1f)),
            order: 800);
        TankardStorage = ConfigEntry(
            "1 - General",
            "Tankard Storage",
            Toggle.On,
            "Allow tankards to store meads in an item-bound inventory opened from the player inventory.",
            order: 700);
        DrinkStoredMeadsOnUse = ConfigEntry(
            "1 - General",
            "Drink Stored Meads On Use",
            Toggle.On,
            "When a tankard is used, drink every stored mead that can currently be consumed before falling back to normal inventory meads.",
            order: 600);
        MaxTankardsInInventory = ConfigEntry(
            "1 - General",
            "Max Tankards In Inventory",
            3,
            new ConfigDescription("Maximum total tankards allowed in the player inventory. 0 disables this limit.", new AcceptableValueRange<int>(0, 100)),
            order: 500);

        EnableMod.SettingChanged += OnConfigChanged;
        MovementWhileDrinking.SettingChanged += OnConfigChanged;
        TankardStorage.SettingChanged += OnConfigChanged;
        DrinkStoredMeadsOnUse.SettingChanged += OnConfigChanged;

        TankardLocalization.Register();

        TankardTweaks.RegisterProfile(
            this,
            "2 - Tankard",
            "Tankard",
            durability: 10,
            cooldownReduction: 0.10f,
            durationBonus: 0.10f,
            storageSlots: 3);
        TankardTweaks.RegisterProfile(
            this,
            "3 - Anniversary Tankard",
            "TankardAnniversary",
            durability: 15,
            cooldownReduction: 0.20f,
            durationBonus: 0.20f,
            storageSlots: 4);
        TankardTweaks.RegisterProfile(
            this,
            "4 - Dvergr Tankard",
            "Tankard_dvergr",
            durability: 20,
            cooldownReduction: 0.30f,
            durationBonus: 0.30f,
            storageSlots: 5);

        _harmony.PatchAll();
    }

    private static void OnConfigChanged(object sender, EventArgs args)
    {
        TankardTweaks.ApplyItemDefinitions();
    }

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
    }

    internal ConfigEntry<T> ConfigEntry<T>(
        string group,
        string name,
        T value,
        ConfigDescription description,
        bool synchronizedSetting = true,
        int? order = null)
    {
        object[] tags = description.Tags ?? Array.Empty<object>();
        if (order != null)
        {
            object[] orderedTags = new object[tags.Length + 1];
            Array.Copy(tags, orderedTags, tags.Length);
            orderedTags[tags.Length] = new ConfigurationManagerAttributes { Order = order.Value };
            tags = orderedTags;
        }

        ConfigDescription extendedDescription = new(
            description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"),
            description.AcceptableValues,
            tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
        return configEntry;
    }

    internal ConfigEntry<T> ConfigEntry<T>(
        string group,
        string name,
        T value,
        string description,
        bool synchronizedSetting = true,
        int? order = null)
    {
        return ConfigEntry(group, name, value, new ConfigDescription(description), synchronizedSetting, order);
    }

    private sealed class ConfigurationManagerAttributes
    {
        public int? Order = null;
    }
}
