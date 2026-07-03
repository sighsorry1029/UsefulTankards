using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace UsefulTankards;

[BepInPlugin(ModGuid, ModName, ModVersion)]
public sealed class UsefulTankardsPlugin : BaseUnityPlugin
{
    public const string ModName = "UsefulTankards";
    public const string ModVersion = "0.1.0";
    public const string Author = "sighsorry";
    public const string ModGuid = Author + "." + ModName;

    internal static ManualLogSource Log = null!;

    internal static ConfigEntry<bool> EnableMod = null!;
    internal static ConfigEntry<bool> MovementWhileDrinking = null!;
    internal static ConfigEntry<bool> TankardStorage = null!;
    internal static ConfigEntry<bool> DrinkStoredMeadsOnUse = null!;
    internal static ConfigEntry<int> MaxTankardsInInventory = null!;

    private readonly Harmony _harmony = new(ModGuid);

    private void Awake()
    {
        Log = Logger;

        EnableMod = Config.Bind("01 - General", "Enable", true, "Enable UsefulTankards tweaks.");
        MovementWhileDrinking = Config.Bind("01 - General", "Movement While Drinking", true, "Allow movement and rotation while drinking through a tankard.");
        TankardStorage = Config.Bind("01 - General", "Tankard Storage", true, "Allow tankards to store meads in an item-bound inventory opened from the player inventory.");
        DrinkStoredMeadsOnUse = Config.Bind("01 - General", "Drink Stored Meads On Use", true, "When a tankard is used, drink every stored mead that can currently be consumed before falling back to normal inventory meads.");
        MaxTankardsInInventory = Config.Bind(
            "01 - General",
            "Max Tankards In Inventory",
            3,
            new ConfigDescription("Maximum total tankards allowed in the player inventory. 0 disables this limit.", new AcceptableValueRange<int>(0, 100)));

        EnableMod.SettingChanged += OnConfigChanged;
        MovementWhileDrinking.SettingChanged += OnConfigChanged;
        TankardStorage.SettingChanged += OnConfigChanged;
        DrinkStoredMeadsOnUse.SettingChanged += OnConfigChanged;

        TankardLocalization.Register();

        TankardTweaks.RegisterProfile(
            Config,
            "02 - Tankard",
            "Tankard",
            durability: 10,
            cooldownReduction: 0.10f,
            durationBonus: 0.10f,
            storageSlots: 3);
        TankardTweaks.RegisterProfile(
            Config,
            "03 - Anniversary Tankard",
            "TankardAnniversary",
            durability: 15,
            cooldownReduction: 0.20f,
            durationBonus: 0.20f,
            storageSlots: 4);
        TankardTweaks.RegisterProfile(
            Config,
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

    private void OnDestroy()
    {
        _harmony.UnpatchSelf();
    }
}
