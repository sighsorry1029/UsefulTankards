using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

// Runs under the game's Mono, invoking the original item reader and the final mod's
// pure durability guard. It does not run Inventory.Load, prefabs, Harmony or gameplay.
internal static class DurabilityMono
{
    private static int _assertions;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool PreviousComparison(int serialized, float restored)
    {
        return restored == (float)serialized * 0.01f;
    }

    public static int Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("<Managed directory> <mod DLL> <BepInEx core directory>");
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            foreach (string directory in new[] { args[0], Path.GetDirectoryName(Path.GetFullPath(args[1])), args[2] })
            {
                string path = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };

        Assembly game = Assembly.LoadFrom(Path.Combine(args[0], "assembly_valheim.dll"));
        Type packageType = game.GetType("ZPackage", true);
        Type itemType = game.GetType("ItemDrop+ItemData", true);
        Type versionType = game.GetType("Version+Item", true);
        MethodInfo load = itemType.GetMethod("Load", new[] { packageType, versionType });
        Type storage = Assembly.LoadFrom(Path.GetFullPath(args[1])).GetType("UsefulTankards.TankardStorageSystem", true);
        MethodInfo guard = storage.GetMethod("IsStoredDurabilityUnchanged", BindingFlags.NonPublic | BindingFlags.Static);
        if (guard == null) throw new Exception("The mod does not contain the runtime-safe durability guard.");

        int previousFailures = 0;
        foreach (int raw in new[] { 0, 1, 17, 100, 1001, 1000, 9999, 10000, 12345, 100000, 1234567 })
        {
            object package = Activator.CreateInstance(packageType, new object[] { EncodeItem(raw) });
            object tuple = load.Invoke(null, new[] { package, Enum.ToObject(versionType, 109) });
            object item = tuple.GetType().GetField("Item2").GetValue(tuple);
            float restored = (float)itemType.GetField("m_durability").GetValue(item);
            if (!PreviousComparison(raw, restored)) previousFailures++;
            Check((bool)guard.Invoke(null, new object[] { raw, restored }), "original load rejected for " + raw);
            Check(!(bool)guard.Invoke(null, new object[] { raw, restored + 0.25f }), "changed durability accepted for " + raw);
            // Repeated no-change saves can quantize a hundredth away in the game itself.
            // Each resulting payload must still reopen using its own serialized value.
            for (int round = 0; round < 3; round++)
            {
                int nextRaw = (int)(restored * 100f);
                package = Activator.CreateInstance(packageType, new object[] { EncodeItem(nextRaw) });
                tuple = load.Invoke(null, new[] { package, Enum.ToObject(versionType, 109) });
                item = tuple.GetType().GetField("Item2").GetValue(tuple);
                restored = (float)itemType.GetField("m_durability").GetValue(item);
                Check((bool)guard.Invoke(null, new object[] { nextRaw, restored }), "repeated reopen rejected");
            }
        }
        foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100.001f, 99.999f })
            Check(!(bool)guard.Invoke(null, new object[] { 10000, invalid }), "invalid or changed value accepted");
        if ((Environment.GetEnvironmentVariable("USEFULTANKARDS_MONO_OPTIONS") ?? "").Contains("-float32"))
            Check(previousFailures > 0, "the old precision bug was not reproduced");
        Console.WriteLine("PASS {0} assertions with original ItemData.Load and final DLL guard; old comparison rejected {1}/11 normal loads.", _assertions, previousFailures);
        return 0;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _assertions++;
    }

    private static string EncodeItem(int durability)
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(durability);
            writer.Write((byte)0); // x
            writer.Write((byte)0); // y
            writer.Write((byte)0); // world level
            writer.Write((byte)64); // default quality/stack, prefab hash present
            writer.Write(1); // reader does not instantiate or resolve the prefab
            writer.Write((byte)0); // extra flags
            return Convert.ToBase64String(stream.ToArray());
        }
    }
}
