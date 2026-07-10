using System;
using System.Reflection;
using HarmonyLib;

namespace UsefulTankards;

internal static class TankardLocalization
{
    internal const string OpenHintKey = "$ut_tankard_open_hint";
    internal const string CooldownReductionKey = "$ut_tankard_cooldown_reduction";
    internal const string BuffDurationBonusKey = "$ut_tankard_buff_duration_bonus";
    internal const string StoredMeadsKey = "$ut_tankard_stored_meads";

    private const string EnglishLanguage = "english";
    private const string KoreanLanguage = "korean";
    private static readonly MethodInfo? AddWordMethod = AccessTools.Method(typeof(Localization), "AddWord", new[] { typeof(string), typeof(string) });
    private static bool _addWordUnavailable;
    private static bool _addWordFailureLogged;

    private static readonly (string Key, string English, string Korean)[] Translations =
    {
        (OpenHintKey, "Press <b>$KEY_Use</b> to open tankard storage.", "<b>$KEY_Use</b>를 눌러 탱커드 저장소를 엽니다."),
        (CooldownReductionKey, "Potion cooldown reduction: {0}%", "포션 재사용 대기시간 감소: {0}%"),
        (BuffDurationBonusKey, "Buff duration bonus: {0}%", "버프 지속시간 증가: {0}%"),
        (StoredMeadsKey, "Stored meads:", "저장된 벌꿀주:"),
    };

    internal static void Register()
    {
        if (Localization.instance != null)
        {
            Register(Localization.instance);
        }
    }

    internal static void Register(Localization? localization)
    {
        if (localization == null)
        {
            return;
        }

        string languageName = NormalizeLanguageName(localization.GetSelectedLanguage());
        bool useKorean = languageName == KoreanLanguage;
        foreach ((string key, string english, string korean) in Translations)
        {
            if (!AddWord(
                    localization,
                    key.Substring(1),
                    useKorean ? korean : english))
            {
                return;
            }
        }
    }

    internal static string Localize(string key)
    {
        if (Localization.instance == null)
        {
            return key;
        }

        string localized = Localization.instance.Localize(key);
        return localized.Contains("$") ? Localization.instance.Localize(localized) : localized;
    }

    private static string NormalizeLanguageName(string? languageName)
    {
        return string.IsNullOrWhiteSpace(languageName)
            ? EnglishLanguage
            : languageName!.Trim().Replace(" ", string.Empty).ToLowerInvariant();
    }

    private static bool AddWord(Localization localization, string key, string value)
    {
        if (_addWordUnavailable)
        {
            return false;
        }

        if (AddWordMethod == null)
        {
            DisableAddWordRegistration("Localization.AddWord(string, string) was not found.");
            return false;
        }

        try
        {
            AddWordMethod.Invoke(localization, new object[] { key, value });
            return true;
        }
        catch (Exception exception)
        {
            Exception cause = exception.GetBaseException();
            LogAddWordFailureOnce($"Localization.AddWord failed: {cause.GetType().Name}: {cause.Message}");
            return false;
        }
    }

    private static void DisableAddWordRegistration(string reason)
    {
        _addWordUnavailable = true;
        LogAddWordFailureOnce(reason);
    }

    private static void LogAddWordFailureOnce(string reason)
    {
        if (_addWordFailureLogged)
        {
            return;
        }

        _addWordFailureLogged = true;
        UsefulTankardsPlugin.Log.LogError($"Could not register UsefulTankards localization. {reason}");
    }
}

[HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
internal static class UsefulTankardsLocalizationSetupLanguagePatch
{
    private static void Postfix(Localization __instance)
    {
        TankardLocalization.Register(__instance);
    }
}
