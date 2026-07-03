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
    internal static ConfigEntry<float> MovementWhileDrinking = null!;
    internal static ConfigEntry<int> MaxTankardsInInventory = null!;

    internal static bool ModEnabled => true;
    internal static float MovementWhileDrinkingMultiplier => Math.Min(1f, Math.Max(0f, MovementWhileDrinking.Value));
    internal static bool TankardStorageEnabled => true;
    internal static bool DrinkStoredMeadsOnUseEnabled => true;

    private readonly Harmony _harmony = new(ModGuid);
    private static bool _roundingMovementWhileDrinking;

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

        MovementWhileDrinking = ConfigEntry(
            "1 - General",
            "Movement While Drinking",
            0.5f,
            new ConfigDescription(
                "Movement and rotation speed multiplier while drinking through a tankard. 0 keeps vanilla movement lock; 1 allows normal movement.",
                new AcceptableValueRange<float>(0f, 1f)),
            order: 900);
        MaxTankardsInInventory = ConfigEntry(
            "1 - General",
            "Max Tankards In Inventory",
            3,
            new ConfigDescription("Maximum total tankards allowed in the player inventory. 0 disables this limit.", new AcceptableValueRange<int>(0, 100)),
            order: 800);

        RoundMovementWhileDrinking();
        MovementWhileDrinking.SettingChanged += OnMovementWhileDrinkingChanged;

        TankardLocalization.Register();

        TankardTweaks.RegisterProfile(
            this,
            "02 - Tankard",
            "Tankard",
            durability: 10,
            cooldownReduction: 0.10f,
            durationBonus: 0.10f,
            storageSlots: 3);
        TankardTweaks.RegisterProfile(
            this,
            "03 - Anniversary Tankard",
            "TankardAnniversary",
            durability: 15,
            cooldownReduction: 0.20f,
            durationBonus: 0.20f,
            storageSlots: 4);
        TankardTweaks.RegisterProfile(
            this,
            "04 - Dvergr Tankard",
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

    private static void OnMovementWhileDrinkingChanged(object sender, EventArgs args)
    {
        RoundMovementWhileDrinking();
    }

    private static void RoundMovementWhileDrinking()
    {
        if (MovementWhileDrinking == null || _roundingMovementWhileDrinking)
        {
            return;
        }

        float clamped = Math.Min(1f, Math.Max(0f, MovementWhileDrinking.Value));
        float rounded = (float)Math.Round(clamped, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(MovementWhileDrinking.Value - rounded) <= 0.0001f)
        {
            return;
        }

        try
        {
            _roundingMovementWhileDrinking = true;
            MovementWhileDrinking.Value = rounded;
        }
        finally
        {
            _roundingMovementWhileDrinking = false;
        }
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
