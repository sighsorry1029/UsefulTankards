using System.Reflection;
using HarmonyLib;

namespace UsefulTankards;

internal static class ValheimAccess
{
    private static readonly FieldInfo? InventoryGuiCurrentContainerField = AccessTools.Field(typeof(InventoryGui), "m_currentContainer");
    private static readonly FieldInfo? InventoryGuiPlayerGridField = AccessTools.Field(typeof(InventoryGui), "m_playerGrid");
    private static readonly FieldInfo? ContainerNameField = AccessTools.Field(typeof(Container), "m_name");
    private static readonly FieldInfo? ContainerWidthField = AccessTools.Field(typeof(Container), "m_width");
    private static readonly FieldInfo? ContainerHeightField = AccessTools.Field(typeof(Container), "m_height");
    private static readonly FieldInfo? ContainerInventoryField = AccessTools.Field(typeof(Container), "m_inventory");
    private static readonly FieldInfo? ContainerInUseField = AccessTools.Field(typeof(Container), "m_inUse");

    internal static Container? GetCurrentContainer(InventoryGui? gui)
    {
        return gui == null ? null : InventoryGuiCurrentContainerField?.GetValue(gui) as Container;
    }

    internal static InventoryGrid? GetPlayerGrid(InventoryGui? gui)
    {
        return gui == null ? null : InventoryGuiPlayerGridField?.GetValue(gui) as InventoryGrid;
    }

    internal static void SetContainerFields(Container container, string name, int width, int height, Inventory inventory, bool inUse)
    {
        ContainerNameField?.SetValue(container, name);
        ContainerWidthField?.SetValue(container, width);
        ContainerHeightField?.SetValue(container, height);
        ContainerInventoryField?.SetValue(container, inventory);
        SetContainerInUse(container, inUse);
    }

    internal static void SetContainerInUse(Container container, bool inUse)
    {
        ContainerInUseField?.SetValue(container, inUse);
    }

    internal static bool GetContainerInUse(Container container)
    {
        return ContainerInUseField?.GetValue(container) as bool? ?? false;
    }
}
