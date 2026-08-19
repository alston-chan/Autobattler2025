using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;

/// <summary>
/// Hollow items: gear whose engraving has been banked (Docs/Resonance.md).
///
/// Engraving used to destroy the item, which was tidy for the slot and bad for the hero — a bow was
/// the reason an archer could shoot at all, so cashing in its engraving could leave them unable to
/// use the kit they were built around until another bow turned up. A hollow item keeps its shape and
/// loses its substance: no stats, no engraving, but still a weapon of its class, so the hero goes on
/// shooting while the slot stays honestly occupied by something that no longer helps.
///
/// Marked with <see cref="ItemModifier.Hollow"/> rather than tracked on the side, because that makes
/// hollowness a property of the ITEM. It survives being moved between bag and equipment (which mints
/// new objects and copies the modifier), it changes the item's hash so it stacks and attunes as its
/// own thing, it is written to a save with the inventory, and it stays hollow if it ever reaches
/// another hero. A set kept on the bearer would get all four of those wrong.
/// </summary>
public static class HollowItems
{
    /// <summary>Whether this item has been spent on an engraving.</summary>
    public static bool IsHollow(Item item) =>
        item != null && item.Modifier != null && item.Modifier.Id == ItemModifier.Hollow;

    /// <summary>
    /// Mark an item spent. Only changes the item itself — stats and engravings are reconciled by
    /// whoever is wearing it, so callers go through <see cref="CharacterInventory.HollowItem"/>
    /// rather than calling this directly.
    /// </summary>
    public static void Hollow(Item item)
    {
        if (item == null) return;
        item.Modifier = new Modifier(ItemModifier.Hollow, 0);
    }
}
