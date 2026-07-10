using System;
using System.Reflection;
using HarmonyLib;

namespace UsefulTankards;

internal static class ValheimAccess
{
    private static readonly FieldInfo InventoryGuiCurrentContainerField = RequireField(typeof(InventoryGui), "m_currentContainer");
    private static readonly FieldInfo InventoryGuiPlayerGridField = RequireField(typeof(InventoryGui), "m_playerGrid");
    private static readonly FieldInfo ContainerNameField = RequireField(typeof(Container), "m_name");
    private static readonly FieldInfo ContainerWidthField = RequireField(typeof(Container), "m_width");
    private static readonly FieldInfo ContainerHeightField = RequireField(typeof(Container), "m_height");
    private static readonly FieldInfo ContainerInventoryField = RequireField(typeof(Container), "m_inventory");
    private static readonly FieldInfo ContainerInUseField = RequireField(typeof(Container), "m_inUse");
    private static readonly FieldInfo InventoryOnChangedField = RequireField(typeof(Inventory), "m_onChanged");
    private static readonly MethodInfo InventoryGuiCloseContainerMethod = RequireMethod(typeof(InventoryGui), nameof(InventoryGui.CloseContainer));
    private static readonly MethodInfo InventoryChangedMethod = RequireMethod(typeof(Inventory), nameof(Inventory.Changed));

    internal static void Validate()
    {
        _ = InventoryChangedMethod.Name;
    }

    internal static Container? GetCurrentContainer(InventoryGui? gui)
    {
        return gui == null ? null : InventoryGuiCurrentContainerField.GetValue(gui) as Container;
    }

    internal static InventoryGrid? GetPlayerGrid(InventoryGui? gui)
    {
        return gui == null ? null : InventoryGuiPlayerGridField.GetValue(gui) as InventoryGrid;
    }

    internal static void SetContainerFields(Container container, string name, int width, int height, Inventory inventory, bool inUse)
    {
        ContainerNameField.SetValue(container, name);
        ContainerWidthField.SetValue(container, width);
        ContainerHeightField.SetValue(container, height);
        ContainerInventoryField.SetValue(container, inventory);
        SetContainerInUse(container, inUse);
    }

    internal static void SetContainerInUse(Container container, bool inUse)
    {
        ContainerInUseField.SetValue(container, inUse);
    }

    internal static void CloseContainer(InventoryGui gui)
    {
        InventoryGuiCloseContainerMethod.Invoke(gui, null);
    }

    internal static void Changed(Inventory? inventory)
    {
        if (inventory != null)
        {
            InventoryChangedMethod.Invoke(inventory, null);
        }
    }

    internal static void AddInventoryChangedHandler(Inventory? inventory, Action handler)
    {
        if (inventory == null)
        {
            return;
        }

        Action? existing = InventoryOnChangedField.GetValue(inventory) as Action;
        InventoryOnChangedField.SetValue(inventory, (Action?)Delegate.Combine(existing, handler));
    }

    internal static void RemoveInventoryChangedHandler(Inventory? inventory, Action handler)
    {
        if (inventory == null)
        {
            return;
        }

        Action? existing = InventoryOnChangedField.GetValue(inventory) as Action;
        InventoryOnChangedField.SetValue(inventory, (Action?)Delegate.Remove(existing, handler));
    }

    private static FieldInfo RequireField(Type type, string name)
    {
        return AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
    }

    private static MethodInfo RequireMethod(Type type, string name)
    {
        return AccessTools.Method(type, name) ?? throw new MissingMethodException(type.FullName, name);
    }
}
