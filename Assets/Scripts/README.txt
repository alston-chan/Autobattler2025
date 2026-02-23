https://github.com/hippogamesunity/HeroEditorHub/wiki/Understanding-inventory-system

Logical layer
Item class is item definition, a logical object that is unique to your game. Usually, Item has Id and Count. Player's items may be stored as a part of a player's profile in PlayerPrefs. Trader's items may be constant ("hard-coded") or generated at runtime based on the player's progress.

ItemParams class is item description. It may contain information about items that are unique to your game, like type, weight, price, attributes and other. A full list of all item parameters is some kind of item database and is usually constant (stored in game resources).

ItemCollection is a scriptable object stored in Resources that contains a list of ItemParams. You can use it if you want, or just make your own implementation.

Ok, now we have separate separate Item and ItemParams classes. Knowing an item's ID we can find its' params at any time.

While Item and ItemParams are LOGICAL objects, now we want to get some VISUAL things like equip items on characters or implement an inventory system.

Visual layer
SpriteCollection is a list of all sprites available in Hero Editor. It's also a scriptable object. ItemParams class contains SpriteId property. Using it you can find ItemSprite in SpriteCollection.

IconCollection is a list of all icons available in Hero Editor. Icons are usually used in inventory systems. Please note, that sprites from SpriteCollection may be associated with multiple icons. For example, an armor sprite (atlas) has helmet, vest, bracers and leggings icons. ItemParams class contains IconId property. Using it you can find ItemIcon in IconCollection.

Workflow
Finally, our workflow is:

Define all ItemParams in ItemCollection scriptable object that is stored in Resources. You can use any external sources like Google Sheets, for example.
Define player's profile and add a new property List<Item> Items, it's a list of items that a player owns.
Knowing Item.Id find ItemParams in ItemCollection
Knowing ItemParams.SpriteId find ItemSprite in SpriteCollection and equip characters
Knowing ItemParams.IconId find ItemIcon in IconCollection and show in inventory
Notes
IconCollection has a static field Active that should be assigned from you main script. After this, you can use Active property anywhere with no need to have direct references to collections.
ItemCollection can be linked with multiple instance of SpriteCollection and IconCollection. For example, you can merge fantasy and military sprites and icons to a single item collection.