// External contract doubles for the linked production adapter. No Harmony detours or
// EpicLoot shortcut implementation is simulated; tests prescribe its GUI decision.
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch(Type type, string name, Type[]? args = null) : Attribute
    { public Type Target = type; public string Method = name; public Type[]? Args = args; }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPriority(int priority) : Attribute { public int Priority = priority; }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyBefore(string owner) : Attribute { public string Owner = owner; }
    public static class Priority { public const int First = 800; }
}
namespace BepInEx.Bootstrap
{
    public sealed class PluginInfo { public object Instance = new EpicLoot.TestPlugin(); }
    public static class Chainloader { public static readonly Dictionary<string, PluginInfo> PluginInfos = new(); }
}
namespace EpicLoot
{
    public sealed class TestPlugin { }
    public static class InventoryPromptHost
    {
        public static bool Open, Throw;
        public static bool IsOpen => Throw ? throw new InvalidOperationException("Injected prompt failure") : Open;
    }
    public static class API
    {
        public static readonly Dictionary<string, Func<ItemDrop.ItemData, bool>> Providers = new();
        public static bool Reject, ThrowAfterRegister, ThrowUnregister, MissingCategory;
        public static int RegisterCalls, UnregisterCalls;
        public static bool RegisterItemContainer(string id, Func<ItemDrop.ItemData, bool> predicate)
        {
            RegisterCalls++;
            if (Reject || Providers.ContainsKey(id)) return false;
            Providers.Add(id, predicate);
            if (ThrowAfterRegister) throw new InvalidOperationException("Injected registration event failure");
            return true;
        }
        public static bool UnregisterItemContainer(string id)
        {
            UnregisterCalls++;
            if (ThrowUnregister) throw new InvalidOperationException("Injected unregister failure");
            return Providers.Remove(id);
        }
        public static Dictionary<string, List<string>> GetRegisteredProviders() => MissingCategory
            ? new() : new() { ["ItemContainer"] = Providers.Keys.ToList() };
    }
}
namespace EpicLoot.ShardStones
{
    internal static class SocketOverlayInput
    {
        internal static bool Press, Throw;
        internal static InventoryGui? LastGui;
        internal static bool IsShardSlotPress(InventoryGui gui)
        {
            LastGui = gui;
            if (Throw) throw new InvalidOperationException("Injected shortcut failure");
            return Press;
        }
        // A trap overload proves that the adapter chooses the GUI-aware contract.
        internal static bool IsShardSlotPress(ItemDrop.ItemData item) => throw new InvalidOperationException("Wrong item-only overload");
    }
}
