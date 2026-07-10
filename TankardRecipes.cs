using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace UsefulTankards;

internal static class TankardRecipes
{
    private static readonly Dictionary<string, TankardRecipeProfile> Profiles = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WarnedMessages = new(StringComparer.OrdinalIgnoreCase);

    internal static void RegisterRecipe(
        UsefulTankardsPlugin plugin,
        string section,
        string prefabName,
        string defaultStation,
        string defaultResources)
    {
        TankardRecipeProfile profile = new TankardRecipeProfile(
            prefabName,
            plugin.ConfigEntry(
                section,
                "Recipe Enabled",
                UsefulTankardsPlugin.Toggle.On,
                "If on, UsefulTankards updates this tankard recipe. If off, the recipe is disabled.",
                order: 950),
            plugin.ConfigEntry(
                section,
                "Recipe Station",
                defaultStation,
                "Crafting station and minimum level for this tankard recipe. Format: StationPrefab, Level. Use None, 1 for hand crafting.",
                order: 940),
            plugin.ConfigEntry(
                section,
                "Recipe Resources",
                defaultResources,
                "Resources required to craft this tankard. Format: Item:Amount, OtherItem:Amount. Amounts may be zero; leave the value empty for no resource requirements.",
                order: 930));

        profile.Enabled.SettingChanged += (_, _) => ApplyRecipeDefinitions();
        profile.Station.SettingChanged += (_, _) => ApplyRecipeDefinitions();
        profile.Resources.SettingChanged += (_, _) => ApplyRecipeDefinitions();
        Profiles[prefabName] = profile;
    }

    internal static void ApplyRecipeDefinitions()
    {
        if (ObjectDB.instance == null || ZNetScene.instance == null)
        {
            return;
        }

        foreach (TankardRecipeProfile profile in Profiles.Values)
        {
            ApplyRecipeDefinition(profile);
        }

        RefreshLocalCraftingUi();
    }

    private static void ApplyRecipeDefinition(TankardRecipeProfile profile)
    {
        if (profile.Enabled.Value != UsefulTankardsPlugin.Toggle.On)
        {
            Recipe? disabledRecipe = FindRecipe(profile.PrefabName);
            if (disabledRecipe is null || (UnityEngine.Object)(object)disabledRecipe == null)
            {
                ItemDrop? disabledItem = ResolveItemDrop(profile.PrefabName);
                if (disabledItem is null || (UnityEngine.Object)(object)disabledItem == null)
                {
                    return;
                }

                disabledRecipe = CreateRecipe(profile.PrefabName, disabledItem);
            }

            disabledRecipe.m_enabled = false;
            return;
        }

        ItemDrop? item = ResolveItemDrop(profile.PrefabName);
        if (item is null || (UnityEngine.Object)(object)item == null)
        {
            DisableExistingRecipe(profile.PrefabName);
            WarnOnce($"Could not apply recipe for '{profile.PrefabName}': item prefab was not found.");
            return;
        }

        if (!TryParseCraftingStationWithLevel(profile.Station.Value, out string stationName, out int stationLevel) ||
            !TryResolveCraftingStation(stationName, out CraftingStation? station))
        {
            DisableExistingRecipe(profile.PrefabName);
            WarnOnce($"Could not apply recipe for '{profile.PrefabName}': crafting station '{profile.Station.Value}' was invalid or not found.");
            return;
        }

        if (!TryParseRequirements(profile.Resources.Value, out Piece.Requirement[] requirements))
        {
            DisableExistingRecipe(profile.PrefabName);
            WarnOnce($"Could not apply recipe for '{profile.PrefabName}': resources '{profile.Resources.Value}' were invalid.");
            return;
        }

        Recipe recipe = FindRecipe(profile.PrefabName) ?? CreateRecipe(profile.PrefabName, item);
        recipe.m_item = item;
        recipe.m_amount = 1;
        recipe.m_enabled = true;
        recipe.m_craftingStation = station;
        recipe.m_minStationLevel = stationLevel;
        recipe.m_requireOnlyOneIngredient = false;
        recipe.m_qualityResultAmountMultiplier = 1f;
        recipe.m_resources = requirements;
    }

    private static Recipe? FindRecipe(string prefabName)
    {
        string recipeName = GetRecipeName(prefabName);
        foreach (Recipe recipe in ObjectDB.instance.m_recipes)
        {
            if ((UnityEngine.Object)(object)recipe == null)
            {
                continue;
            }

            if (string.Equals(((UnityEngine.Object)recipe).name, recipeName, StringComparison.OrdinalIgnoreCase))
            {
                return recipe;
            }
        }

        return null;
    }

    private static void DisableExistingRecipe(string prefabName)
    {
        Recipe? recipe = FindRecipe(prefabName);
        if (recipe is not null && (UnityEngine.Object)(object)recipe != null)
        {
            recipe.m_enabled = false;
        }
    }

    private static Recipe CreateRecipe(string prefabName, ItemDrop item)
    {
        Recipe recipe = ScriptableObject.CreateInstance<Recipe>();
        ((UnityEngine.Object)recipe).name = GetRecipeName(prefabName);
        recipe.m_item = item;
        ObjectDB.instance.m_recipes.Add(recipe);
        return recipe;
    }

    private static bool TryParseRequirements(string? definition, out Piece.Requirement[] requirements)
    {
        List<Piece.Requirement> parsed = new();
        requirements = Array.Empty<Piece.Requirement>();

        foreach (string rawToken in (definition ?? "").Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = rawToken.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            string[] parts = token.Split(':');
            if (parts.Length != 2)
            {
                return false;
            }

            string itemName = NormalizePrefabName(parts[0]);
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount) || amount < 0)
            {
                return false;
            }

            ItemDrop? item = ResolveItemDrop(itemName);
            if (item is null || (UnityEngine.Object)(object)item == null)
            {
                WarnOnce($"Could not resolve recipe resource item '{itemName}'.");
                return false;
            }

            parsed.Add(new Piece.Requirement
            {
                m_resItem = item,
                m_amount = amount,
                m_amountPerLevel = 1,
                m_recover = true
            });
        }

        requirements = parsed.ToArray();
        return true;
    }

    private static bool TryParseCraftingStationWithLevel(string? value, out string station, out int level)
    {
        station = "None";
        level = 1;

        string[] parts = (value ?? "").Split(',');
        if (parts.Length > 2)
        {
            return false;
        }

        station = NormalizePrefabName(parts[0]);
        if (station.Length == 0)
        {
            station = "None";
        }

        return parts.Length != 2 ||
               (int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out level) && level >= 1);
    }

    private static bool TryResolveCraftingStation(string value, out CraftingStation? station)
    {
        station = null;
        string normalized = NormalizePrefabName(value);
        if (IsNone(normalized))
        {
            return true;
        }

        GameObject? prefab = ResolvePrefab(normalized);
        if (prefab == null || !prefab.TryGetComponent(out CraftingStation resolvedStation))
        {
            return false;
        }

        station = resolvedStation;
        return true;
    }

    private static ItemDrop? ResolveItemDrop(string itemName)
    {
        string normalized = NormalizePrefabName(itemName);
        GameObject? prefab = ObjectDB.instance?.GetItemPrefab(normalized);
        if (prefab == null)
        {
            prefab = ResolvePrefab(normalized);
        }

        return prefab != null && prefab.TryGetComponent(out ItemDrop itemDrop) ? itemDrop : null;
    }

    private static GameObject? ResolvePrefab(string prefabName)
    {
        string normalized = NormalizePrefabName(prefabName);
        if (ZNetScene.instance != null)
        {
            GameObject? prefab = ZNetScene.instance.GetPrefab(normalized);
            if (prefab != null)
            {
                return prefab;
            }
        }

        return ObjectDB.instance?.GetItemPrefab(normalized);
    }

    private static void RefreshLocalCraftingUi()
    {
        if (Player.m_localPlayer == null)
        {
            return;
        }

        Player.m_localPlayer.UpdateKnownRecipesList();
        if (InventoryGui.instance != null)
        {
            InventoryGui.instance.UpdateCraftingPanel();
            InventoryGui.instance.UpdateRecipe(Player.m_localPlayer, 0f);
        }
    }

    private static void WarnOnce(string message)
    {
        if (WarnedMessages.Add(message))
        {
            UsefulTankardsPlugin.Log.LogWarning(message);
        }
    }

    private static string GetRecipeName(string itemName)
    {
        return "Recipe_" + NormalizePrefabName(itemName);
    }

    private static bool IsNone(string value)
    {
        return value.Length == 0 ||
               string.Equals(value, "None", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "null", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePrefabName(string? value)
    {
        value = (value ?? "").Trim();
        const string cloneSuffix = "(Clone)";
        return value.EndsWith(cloneSuffix, StringComparison.Ordinal)
            ? value.Substring(0, value.Length - cloneSuffix.Length).Trim()
            : value;
    }

    private sealed class TankardRecipeProfile
    {
        internal TankardRecipeProfile(
            string prefabName,
            ConfigEntry<UsefulTankardsPlugin.Toggle> enabled,
            ConfigEntry<string> station,
            ConfigEntry<string> resources)
        {
            PrefabName = prefabName;
            Enabled = enabled;
            Station = station;
            Resources = resources;
        }

        internal string PrefabName { get; }
        internal ConfigEntry<UsefulTankardsPlugin.Toggle> Enabled { get; }
        internal ConfigEntry<string> Station { get; }
        internal ConfigEntry<string> Resources { get; }
    }
}
