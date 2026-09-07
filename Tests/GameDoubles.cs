// Deliberately small adapters for the game boundary, not a replacement Valheim implementation.
using UnityEngine;

namespace UnityEngine
{
    public class Object
    {
        public string name = "";
        public static void Destroy(Object value) { }
    }

    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }

    public class MonoBehaviour : Component { }
    public sealed class Transform { public Vector3 position; }
    public struct Vector3 { public float x, y, z; }
    public sealed class Sprite : Object { }

    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, Component> _components = new();
        public readonly Transform transform = new();
        public GameObject(string value = "") { name = value; }
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new() { gameObject = this };
            _components[typeof(T)] = component;
            return component;
        }
        public T GetComponent<T>() where T : Component => (_components.GetValueOrDefault(typeof(T)) as T)!;
        public bool TryGetComponent<T>(out T component) where T : Component
        {
            component = GetComponent<T>()!;
            return component != null;
        }
    }

    public static class Mathf
    {
        public static int RoundToInt(float value) => (int)MathF.Round(value);
        public static int CeilToInt(float value) => (int)MathF.Ceiling(value);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    }
    public static class Input { public static Vector3 mousePosition; }
    public static class Time { public static int frameCount; }
}

public readonly record struct Vector2i(int x, int y);

public sealed class ZPackage
{
    private readonly MemoryStream _stream;
    private readonly BinaryReader _reader;
    internal readonly BinaryWriter Writer;
    public ZPackage() : this(Array.Empty<byte>()) { }
    public ZPackage(string encoded) : this(Convert.FromBase64String(encoded)) { }
    private ZPackage(byte[] bytes)
    {
        _stream = new MemoryStream();
        _stream.Write(bytes);
        _stream.Position = 0;
        _reader = new BinaryReader(_stream);
        Writer = new BinaryWriter(_stream);
    }
    public int ReadInt() => _reader.ReadInt32();
    public float ReadSingle() => _reader.ReadSingle();
    public Vector2i ReadVector2i() => new(_reader.ReadInt32(), _reader.ReadInt32());
    public bool ReadBool() => _reader.ReadBoolean();
    public long ReadLong() => _reader.ReadInt64();
    public string ReadString() => _reader.ReadString();
    public string GetBase64() => Convert.ToBase64String(_stream.ToArray());
}

public sealed class ItemDrop : Component
{
    public ItemData m_itemData = new();
    public sealed class ItemData
    {
        public SharedData m_shared = new();
        public Dictionary<string, string> m_customData = new();
        public GameObject m_dropPrefab = null!;
        public int m_stack = 1;
        public int m_quality = 1;
        public Vector2i m_gridPos;
        public Sprite GetIcon() => new();
        public ItemData Clone() => new()
        {
            m_shared = m_shared, m_customData = new(m_customData), m_dropPrefab = m_dropPrefab,
            m_stack = m_stack, m_quality = m_quality, m_gridPos = m_gridPos
        };
        public float GetWeight() => m_shared.m_weight * m_stack * (1 + (m_quality - 1) * m_shared.m_scaleWeightByQuality);
        public enum ItemType { None, Consumable }
        public sealed class SharedData
        {
            public string m_name = "", m_ammoType = "";
            public int m_maxStackSize = 10;
            public float m_weight, m_scaleWeightByQuality, m_food;
            public ItemType m_itemType;
            public StatusEffect m_consumeStatusEffect = null!;
        }
    }
}

public sealed class Inventory
{
    private readonly List<ItemDrop.ItemData> _items = new();
    internal Action? OnChanged;
    internal static int LoadCalls;
    internal int Width { get; }
    internal int Height { get; }
    public Inventory(string name, Sprite? icon, int width, int height) { Width = width; Height = height; }
    public List<ItemDrop.ItemData> GetAllItems() => _items;
    public bool ContainsItem(ItemDrop.ItemData item) => _items.Contains(item);
    public void Load(ZPackage package)
    {
        LoadCalls++;
        _items.Clear();
        // Tests prescribe the game loader's outcome, including missing-prefab/partial loads.
        _items.AddRange(InventoryFixtures.LoadedItems[package.GetBase64()].Select(item => item.Clone()));
    }
    public void Save(ZPackage package) => InventoryFixtures.Write(package, 106, _items);
    public bool RemoveItem(ItemDrop.ItemData item, int amount)
    {
        if (!_items.Contains(item) || item.m_stack < amount) return false;
        item.m_stack -= amount;
        if (item.m_stack == 0) _items.Remove(item);
        OnChanged?.Invoke();
        return true;
    }
}

public sealed class Container : Component
{
    public Inventory Inventory = null!;
    public bool InUse;
    public Inventory GetInventory() => Inventory;
}

public sealed class Player : Component
{
    public static Player? m_localPlayer;
    private readonly Inventory _inventory = new("player", null, 8, 4);
    private readonly SEMan _effects = new();
    internal bool AllowEating = true, AllowConsumption = true, Owner = true;
    internal int Consumed, ConsumeAttempts;
    public Inventory GetInventory() => _inventory;
    public SEMan GetSEMan() => _effects;
    public bool IsOwner() => Owner;
    public bool CanEat(ItemDrop.ItemData item, bool showMessages) => AllowEating;
    public bool ConsumeItem(Inventory inventory, ItemDrop.ItemData item)
    {
        ConsumeAttempts++;
        if (!AllowConsumption || !inventory.ContainsItem(item) || item.m_stack < 1) return false;
        // Match the relevant game ordering: effect application precedes removal/Changed.
        _effects.Hashes.Add(item.m_shared.m_consumeStatusEffect.NameHash());
        string category = item.m_shared.m_consumeStatusEffect.m_category;
        if (category.Length > 0) _effects.Categories.Add(category);
        Consumed++;
        return inventory.RemoveItem(item, 1);
    }
}

public sealed class SEMan
{
    internal readonly HashSet<int> Hashes = new();
    internal readonly HashSet<string> Categories = new();
    public bool HaveStatusEffect(int hash) => Hashes.Contains(hash);
    public bool HaveStatusEffectCategory(string category) => Categories.Contains(category);
}
public sealed class StatusEffect : UnityEngine.Object
{
    public string m_category = "";
    public int NameHash() => name.GetHashCode(StringComparison.Ordinal);
}
public sealed class ObjectDB
{
    public static ObjectDB? instance;
    internal readonly Dictionary<string, GameObject> Prefabs = new(StringComparer.Ordinal);
    internal int LookupCount;
    public GameObject GetItemPrefab(string name) { LookupCount++; return Prefabs.GetValueOrDefault(name)!; }
}
public sealed class InventoryGui : Component
{
    public static InventoryGui instance = null!;
    internal Container? Current;
    internal int StackAllCalls;
    public void Show(Container container, int activeGroup) => Current = container;
}
public sealed class InventoryGrid : Component
{
    public ItemDrop.ItemData GetItem(Vector2i position) => null!;
}
public static class ZInput
{
    public static bool GetButtonDown(string name) => false;
    public static void ResetButtonStatus(string name) { }
}
public sealed class Localization
{
    public static Localization? instance;
    public string Localize(string value) => value;
}

namespace UsefulTankards
{
    internal sealed class TankardProfile { internal int TankardStorageSlots = 3; }
    internal static class TankardTweaks
    {
        internal static readonly Dictionary<ItemDrop.ItemData.SharedData, TankardProfile> Profiles = new();
        internal static bool TryGetProfile(ItemDrop.ItemData? item, out TankardProfile profile)
        {
            profile = null!;
            if (item == null || !Profiles.TryGetValue(item.m_shared, out TankardProfile? found)) return false;
            profile = found;
            return true;
        }
        internal static string GetCleanPrefabName(ItemDrop.ItemData item) => item.m_dropPrefab.name;
    }
    internal static class UsefulTankardsPlugin
    {
        internal static readonly TestLogger Log = new();
        internal sealed class TestLogger
        {
            internal readonly List<string> Warnings = new();
            internal void LogWarning(string message) => Warnings.Add(message);
        }
    }
    internal static class ValheimAccess
    {
        internal static Container? GetCurrentContainer(InventoryGui? gui) => gui?.Current;
        internal static InventoryGrid? GetPlayerGrid(InventoryGui gui) => null;
        internal static void CloseContainer(InventoryGui gui)
        {
            Container? current = gui.Current;
            gui.Current = null;
            // The real integration invokes this via InventoryGui.CloseContainer's Harmony postfix.
            TankardStorageSystem.CloseTankardStorage(current);
        }
        internal static void SetContainerFields(Container container, string name, int width, int height, Inventory inventory, bool inUse)
        {
            container.Inventory = inventory;
            container.InUse = inUse;
        }
        internal static void Changed(Inventory? inventory) => inventory?.OnChanged?.Invoke();
        internal static void StackAll(InventoryGui gui) => gui.StackAllCalls++;
        internal static void AddInventoryChangedHandler(Inventory? inventory, Action handler)
        {
            if (inventory != null) inventory.OnChanged += handler;
        }
        internal static void RemoveInventoryChangedHandler(Inventory? inventory, Action handler)
        {
            if (inventory != null) inventory.OnChanged -= handler;
        }
    }
}
