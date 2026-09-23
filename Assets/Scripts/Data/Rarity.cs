using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Enums;
using UnityEngine;

/// <summary>
/// An item copy's rarity: C, B, A or S (Docs/ShopLoop.md). It is the tier its effect works at while
/// worn and the tier it is engraved at when its quest completes — the three tiers an item used to
/// climb by being worn moved into the shop, where they are a price and a roll.
///
/// Stored on the COPY as <see cref="ItemModifier.Rarity"/>, the way <see cref="HollowItems"/> marks
/// a spent item: the same sword can be C in one offer and S in the next, the modifier survives the
/// moves between bag and equipment (which mint new objects and copy it), it is written to the save
/// with the inventory, and it keys the item's quest progress apart from other copies. The vendor's
/// stat lookup ignores modifiers, so rarity never changes an item's armour or damage — only its effect.
///
/// C is the plain item, with no modifier, so every item authored before rarity existed — kits,
/// loadouts, scenarios — is simply a C.
/// </summary>
public static class Rarity
{
    public const int C = 1, B = 2, A = 3, S = 4;

    /// <summary>The rarity of an item copy, C when it carries none.</summary>
    public static int Of(Item item) =>
        item != null && item.Modifier != null && item.Modifier.Id == ItemModifier.Rarity
            ? Mathf.Clamp(item.Modifier.Level, C, S) : C;

    /// <summary>A new copy of an item at a rarity. C is the plain item.</summary>
    public static Item Make(string itemId, int rarity) =>
        rarity <= C ? new Item(itemId) : new Item(itemId, new Modifier(ItemModifier.Rarity, Mathf.Min(rarity, S)));

    public static string Letter(int rarity) => rarity switch { 2 => "B", 3 => "A", 4 => "S", _ => "C" };

    /// <summary>The colour a rarity is written in: grey, green, blue, purple — the slot backgrounds' hues.</summary>
    public static Color ColorOf(int rarity) => rarity switch
    {
        2 => new Color(0.45f, 0.85f, 0.45f, 1f),
        3 => new Color(0.45f, 0.65f, 1f, 1f),
        4 => new Color(0.78f, 0.5f, 1f, 1f),
        _ => new Color(0.78f, 0.78f, 0.78f, 1f),
    };

    /// <summary>A darker shade of the rarity's colour, for a card face or a panel behind an icon.</summary>
    public static Color CardColorOf(int rarity) => rarity switch
    {
        2 => new Color(0.13f, 0.30f, 0.15f, 0.95f),
        3 => new Color(0.12f, 0.20f, 0.42f, 0.95f),
        4 => new Color(0.30f, 0.14f, 0.42f, 0.95f),
        _ => new Color(0.22f, 0.22f, 0.24f, 0.95f),
    };

    /// <summary>
    /// The slot background an item copy is drawn on: grey C, green B, blue A, purple S, and brown for
    /// a spent (hollow) item. The vendor's own sprites, picked through its GetBackgroundCustom hook
    /// (installed by CharacterInventory), so every inventory, equipment and bag slot shows rarity
    /// without touching the vendor's slot code. It used to pick by the item's authored ItemRarity,
    /// which the game never set and the player never saw a reason for.
    /// </summary>
    public static Sprite Background(Item item)
    {
        var collection = Assets.HeroEditor.InventorySystem.Scripts.ItemCollection.Active;
        if (collection == null || item == null) return null;
        if (HollowItems.IsHollow(item)) return collection.BackgroundBrown;
        return Of(item) switch
        {
            2 => collection.BackgroundGreen,
            3 => collection.BackgroundBlue,
            4 => collection.BackgroundPurple,
            _ => collection.BackgroundGrey,
        };
    }

    /// <summary>"<color=#…>B</color>", for text that is decorated anyway.</summary>
    public static string Tag(int rarity) => "<color=#" + ColorUtility.ToHtmlStringRGB(ColorOf(rarity)) + ">" + Letter(rarity) + "</color>";

    // The starting odds in Docs/ShopLoop.md, to tune: the first fight and the end of the run, with
    // everything between on a straight line.
    private static readonly float[] First = { 0.70f, 0.25f, 0.05f, 0.00f };
    private static readonly float[] Last = { 0.20f, 0.35f, 0.30f, 0.15f };

    /// <summary>The chance of C, B, A and S at a point in the run, 0 at the first fight and 1 at the last.</summary>
    public static float[] OddsAt(float progress)
    {
        float t = Mathf.Clamp01(progress);
        var odds = new float[4];
        for (int i = 0; i < 4; i++) odds[i] = Mathf.Lerp(First[i], Last[i], t);
        return odds;
    }

    /// <summary>A rarity rolled at a point in the run. <paramref name="roll"/> is a number in [0, 1).</summary>
    public static int Roll(float progress, float roll)
    {
        var odds = OddsAt(progress);
        float at = 0f;
        for (int i = 0; i < 4; i++)
        {
            at += odds[i];
            if (roll < at) return i + 1;
        }
        return C;
    }

    public static int Roll(float progress) => Roll(progress, Random.value);
}
