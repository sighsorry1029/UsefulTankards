using Mono.Cecil;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

// Read original metadata without loading Unity. The optional fixture check invokes only
// original ZPackage and ItemData.Load managed code; it does not instantiate game prefabs.
if (args.Length < 2) throw new ArgumentException("GameContracts <mod.dll> <original Managed directory> [fixtures.json] [EpicLoot.dll]");
string gameDir = Path.GetFullPath(args[1]);
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(gameDir);
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0]))!);
using var mod = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters { AssemblyResolver = resolver });
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, "assembly_valheim.dll"));
using var utils = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, "assembly_utils.dll"));
using var guiUtils = AssemblyDefinition.ReadAssembly(Path.Combine(gameDir, "assembly_guiutils.dll"));
IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(AllTypes(t.NestedTypes)));
var gameTypes = AllTypes(game.MainModule.Types).Concat(AllTypes(utils.MainModule.Types))
    .Concat(AllTypes(guiUtils.MainModule.Types)).Where(t => t.Name != "<Module>")
    .GroupBy(t => t.FullName).ToDictionary(g => g.Key, g => g.First());
int references = 0, patches = 0;
foreach (var type in AllTypes(mod.MainModule.Types))
{
    foreach (var method in type.Methods.Where(m => m.HasBody))
    foreach (var instruction in method.Body.Instructions)
    {
        if (instruction.Operand is not MemberReference member || member.DeclaringType == null) continue;
        string scope = member.DeclaringType.Scope.Name;
        if (!File.Exists(Path.Combine(gameDir, scope + ".dll"))) continue;
        if (member is MethodReference mr)
        {
            var definition = mr.Resolve() ?? throw new Exception($"Missing method: {mr}");
            if (scope.StartsWith("assembly_") && !definition.IsPublic) throw new Exception($"Nonpublic direct call: {mr}");
        }
        else if (member is FieldReference fr)
        {
            var definition = fr.Resolve() ?? throw new Exception($"Missing field: {fr}");
            if (definition.IsLiteral) throw new Exception($"Field instruction against constant: {fr}");
            if (scope.StartsWith("assembly_") && !definition.IsPublic) throw new Exception($"Nonpublic direct field: {fr}");
        }
        else continue;
        references++;
    }
    if (type.Namespace != "UsefulTankards") continue;
    foreach (var patch in type.CustomAttributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch"))
    {
        var targetType = gameTypes[((TypeReference)patch.ConstructorArguments[0].Value).FullName];
        string name = (string)patch.ConstructorArguments[1].Value;
        var candidates = targetType.Methods.Where(m => m.Name == name);
        if (patch.ConstructorArguments.Count == 3)
        {
            var signature = ((CustomAttributeArgument[])patch.ConstructorArguments[2].Value)
                .Select(a => ((TypeReference)a.Value).FullName);
            candidates = candidates.Where(m => m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(signature));
        }
        var original = candidates.Single(); // Missing and ambiguous targets both fail.
        foreach (var handler in type.Methods.Where(m => m.Name is "Prefix" or "Postfix" or "Finalizer"))
        foreach (var parameter in handler.Parameters)
        {
            string parameterType = parameter.ParameterType.GetElementType().FullName;
            if (parameter.Name.StartsWith("___"))
            {
                var field = targetType.Fields.Single(f => f.Name == parameter.Name[3..]);
                if (field.FieldType.FullName != parameterType) throw new Exception($"Field injection type mismatch: {handler}");
            }
            else if (parameter.Name == "__result")
            {
                if (original.ReturnType.FullName != parameterType) throw new Exception($"Result mismatch: {handler}");
            }
            else if (!parameter.Name.StartsWith("__"))
            {
                var argument = original.Parameters.Single(p => p.Name == parameter.Name);
                if (argument.ParameterType.GetElementType().FullName != parameterType) throw new Exception($"Argument mismatch: {handler}");
            }
        }
        patches++;
    }
}
// Cached reflection accessors used by ValheimAccess. Check exact signatures, including defaults.
foreach (var contract in new[] {
    ("InventoryGui", "m_currentContainer", "Container"), ("InventoryGui", "m_playerGrid", "InventoryGrid"),
    ("Container", "m_name", "System.String"), ("Container", "m_width", "System.Int32"),
    ("Container", "m_height", "System.Int32"), ("Container", "m_inventory", "Inventory"),
    ("Container", "m_inUse", "System.Boolean"), ("Inventory", "m_onChanged", "System.Action") })
    if (gameTypes[contract.Item1].Fields.Single(f => f.Name == contract.Item2).FieldType.FullName != contract.Item3)
        throw new Exception($"Reflection field mismatch: {contract}");
foreach (var contract in new[] {
    ("InventoryGui", "CloseContainer", ""), ("InventoryGui", "OnStackAll", ""),
    ("Inventory", "Changed", "System.Boolean,System.Boolean"), ("Player", "UpdateKnownRecipesList", ""),
    ("InventoryGui", "UpdateCraftingPanel", "System.Boolean"), ("InventoryGui", "UpdateRecipe", "Player,System.Single"),
    ("Localization", "AddWord", "System.String,System.String") })
    _ = gameTypes[contract.Item1].Methods.Single(m => m.Name == contract.Item2 &&
        string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) == contract.Item3);
Console.WriteLine($"PASS {references} instruction references, {patches} Harmony targets/injections, 15 reflection contracts");

if (args.Length > 3)
{
    using var epic = AssemblyDefinition.ReadAssembly(args[3]);
    foreach (var contract in new[] {
        ("EpicLoot.InventoryPromptHost", "IsOpen"),
        ("EpicLoot.ShardStones.SocketsUI", "IsSocketGridOpen") })
    {
        var getter = epic.MainModule.GetType(contract.Item1).Properties.Single(p => p.Name == contract.Item2).GetMethod;
        if (!getter.IsStatic || getter.Parameters.Count != 0 || getter.ReturnType.FullName != "System.Boolean")
            throw new Exception($"EpicLoot flag delegate contract changed: {contract}");
    }
    var modifier = epic.MainModule.GetType("EpicLoot.Config.ELConfig").Fields.Single(f => f.Name == "SocketOverlayModifier");
    if (!modifier.IsStatic || modifier.FieldType.FullName != "BepInEx.Configuration.ConfigEntry`1<UnityEngine.KeyCode>")
        throw new Exception("EpicLoot modifier contract changed");
    int PriorityOf(Mono.Cecil.ICustomAttributeProvider target) => (int)target.CustomAttributes
        .Single(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPriority").ConstructorArguments[0].Value;
    var ourUpdate = mod.MainModule.GetType("UsefulTankards.UsefulTankardsInventoryGuiUpdatePatch");
    var epicUpdate = epic.MainModule.GetType("EpicLoot.ShardStones.SocketsUI").NestedTypes.Single(t => t.Name == "InventoryGui_Update_Patch");
    // Require the priority on Prefix itself. A class-only value passes a raw
    // metadata check but is lost by the installed HarmonyX's attribute merge.
    if (PriorityOf(ourUpdate.Methods.Single(m => m.Name == "Prefix")) <= PriorityOf(epicUpdate))
        throw new Exception("Tankard input must precede EpicLoot input");
    if (mod.MainModule.AssemblyReferences.Any(r => r.Name is "EpicLoot" or "Newtonsoft.Json"))
        throw new Exception("Optional storage integration introduced a runtime assembly dependency");
    Console.WriteLine("PASS EpicLoot 3 reflection contracts, input priority, and optional assembly boundary (metadata only)");
}

if (args.Length > 2)
{
    AssemblyLoadContext.Default.Resolving += (_, name) =>
        File.Exists(Path.Combine(gameDir, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(gameDir, name.Name + ".dll")) : null;
    var assembly = Assembly.LoadFrom(Path.Combine(gameDir, "assembly_valheim.dll"));
    var packageType = assembly.GetType("ZPackage", true)!;
    var itemType = assembly.GetType("ItemDrop+ItemData", true)!;
    var versionType = assembly.GetType("Version+Item", true)!;
    var load = itemType.GetMethod("Load", new[] { packageType, versionType })!;
    var inventoryType = assembly.GetType("Inventory", true)!;
    var inventory = Activator.CreateInstance(inventoryType, new object[] { "test", null!, 3, 1 })!;
    int notifications = 0;
    inventoryType.GetField("m_onChanged")!.SetValue(inventory, (Action)(() => notifications++));
    inventoryType.GetMethod("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(inventory, new object[] { false, false });
    if (notifications != 1) throw new Exception("Changed did not notify inventory listeners exactly once");
    Console.WriteLine("PASS original private Inventory.Changed(false, false) invocation and callback");
    using var fixtures = JsonDocument.Parse(File.ReadAllText(args[2]));
    int count = 0;
    foreach (var fixture in fixtures.RootElement.EnumerateObject())
    {
        var package = Activator.CreateInstance(packageType, fixture.Name)!;
        object Read(string method) => packageType.GetMethod(method, Type.EmptyTypes)!.Invoke(package, null)!;
        if ((int)Read("ReadInt") != 109 || (ushort)Read("ReadUShort") != fixture.Value.GetArrayLength()) throw new Exception("Header mismatch");
        foreach (var expected in fixture.Value.EnumerateArray())
        {
            var tuple = load.Invoke(null, new[] { package, Enum.ToObject(versionType, 109) })!;
            var item = tuple.GetType().GetField("Item2")!.GetValue(tuple)!;
            if ((int)tuple.GetType().GetField("Item1")!.GetValue(tuple)! != expected.GetProperty("hash").GetInt32()) throw new Exception("Prefab hash mismatch");
            foreach (var field in new[] { "stack", "quality" })
                if ((int)itemType.GetField("m_" + field)!.GetValue(item)! != expected.GetProperty(field).GetInt32()) throw new Exception(field + " mismatch");
            var custom = (Dictionary<string, string>)itemType.GetField("m_customData")!.GetValue(item)!;
            var expectedCustom = expected.GetProperty("customData");
            if (custom.Count != expectedCustom.EnumerateObject().Count() || expectedCustom.EnumerateObject().Any(p => custom[p.Name] != p.Value.GetString())) throw new Exception("Custom data mismatch");
            if (!(bool)itemType.GetField("m_cheated")!.GetValue(item)!) throw new Exception("Cheated flag mismatch");
        }
        if (!Read("GetPos").Equals(Read("Size"))) throw new Exception("Unread item bytes");
        count++;
    }
    Console.WriteLine($"PASS {count} fixtures decoded by original ItemData.Load (managed-only; no Unity prefab or gameplay execution)");
}
