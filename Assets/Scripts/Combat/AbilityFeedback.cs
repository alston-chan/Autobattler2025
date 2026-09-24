using System;
using System.Collections.Generic;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using TMPro;
using UnityEngine;

/// <summary>
/// The feedback for cost abilities (ults): a punctuating flash on the caster as it fires, and a
/// beefier hitstop on each victim. The mana bar emptying is the rest of the signal.
///
/// Nothing floats over the caster by default, as in TFT, where a cast reads through the mana bar and
/// the ability's own effects and its name lives in the unit's tooltip (decided 2026-09-23; it floated
/// the name before). Two switches bring it back: <see cref="Settings.showIcon"/> floats the item the
/// ability comes from, its icon on its rarity's slot — with the name when no item carries it, a
/// monster's trait — and <see cref="Settings.showName"/> floats the name, for debugging.
///
/// Every knob lives on the shared <see cref="CombatFeelSettings"/> asset, so the whole thing can be
/// A/B tested (and, being a ScriptableObject, tuned live in Play mode) like all other combat feel.
/// </summary>
public static class AbilityFeedback
{
    [Serializable]
    public class Settings
    {
        [Header("Over the caster (both off: as in TFT)")]
        [Tooltip("Float the item the ability comes from — its icon on its rarity's slot — above the " +
                 "caster. An ability no item carries (a monster's trait) floats its name instead.")]
        public bool showIcon = false;
        [Tooltip("Debug: float the ability's name above the caster.")]
        public bool showName = false;
        [Tooltip("The icon's size in world units.")]
        public float iconSize = 0.95f;

        [Header("Name")]
        public Color calloutColor = new Color(1f, 0.88f, 0.3f, 1f);
        [Tooltip("Kept close to the damage-number size so callouts read as part of the same layer, " +
                 "not a billboard over the unit.")]
        public float fontSize = 3.75f;
        public Vector3 offset = new Vector3(0f, 1.8f, 0f);
        public float riseSpeed = 1.1f;
        public float lifetime = 1.1f;
        [Tooltip("The callout arrives this much too big and settles to its size, overshooting a little " +
                 "on the way — the label as a whole, never its letters (Docs/Juice.md).")]
        [Range(1f, 2f)] public float punchScale = 1.35f;
        [Tooltip("How long the arrival takes. Short: it is a punctuation mark, not an animation.")]
        public float punchSeconds = 0.18f;
        [Tooltip("Dark edge so the name reads on any terrain (same reasoning as damage numbers).")]
        [Range(0f, 1f)] public float outlineWidth = 0.5f;
        public Color outlineColor = Color.black;

        [Header("Cast punctuation")]
        [Tooltip("Flash the caster as the ability fires, reusing the HitFeedback flash shader.")]
        public bool flashCaster = true;
        public Color flashColor = new Color(1f, 0.95f, 0.6f, 1f);
        public float flashDuration = 0.16f;

        [Header("Impact")]
        [Tooltip("Freeze applied to a victim on each ability hit — this is what makes an ult land " +
                 "heavy versus a basic attack. Spells call AbilityFeedback.Impact() at their hit frame.")]
        public bool enableHitstop = true;
        public float hitstop = 0.12f;
    }

    private static Settings S => CombatFeelSettings.Active.abilityFeedback;

    /// <summary>A verb cast: announced as the weapon that teaches it, worn or banked.</summary>
    public static void AnnounceSpell(Entity caster, Spell spell)
    {
        if (caster == null || spell == null) return;
        Announce(caster, spell.DisplayName, caster.Resonance != null ? caster.Resonance.ItemTeaching(spell) : null);
    }

    /// <summary>An engraving firing: announced as the item that carries it, worn or banked.</summary>
    public static void AnnounceEngraving(Entity owner, Engraving engraving, string label = null)
    {
        if (owner == null || engraving == null) return;
        Announce(owner, label ?? engraving.DisplayName, owner.Resonance != null ? owner.Resonance.ItemOf(engraving) : null);
    }

    /// <summary>
    /// Fire the on-cast feedback: the caster flash, and whatever the settings float over the caster —
    /// the icon of <paramref name="source"/>, the name, both, or nothing.
    /// </summary>
    public static void Announce(Entity caster, string abilityName, Item source = null)
    {
        if (caster == null) return;
        var s = S;

        if (s.flashCaster && caster.HitFeedback != null)
            caster.HitFeedback.Flash(s.flashColor, s.flashDuration);

        var at = caster.transform.position + s.offset;
        bool iconShown = s.showIcon && source != null && AbilityIconCallout.Show(at, source, s);

        // The name: asked for, or standing in for an icon that was asked for and has no item.
        bool name = s.showName || (s.showIcon && !iconShown);
        if (name && !string.IsNullOrEmpty(abilityName))
            AbilityCallout.Show(iconShown ? at + Vector3.up * s.iconSize * 0.75f : at, abilityName, s);
    }

    /// <summary>Fire the per-hit feedback: a heavy hitstop on the victim.</summary>
    public static void Impact(Entity target)
    {
        if (target == null) return;
        var s = S;
        if (s.enableHitstop && s.hitstop > 0f) target.ApplyHitstop(s.hitstop);
    }
}

/// <summary>
/// A single floating ability-name label. Created on demand by <see cref="AbilityFeedback.Announce"/>
/// and self-destructs — ults are infrequent, so it isn't pooled like the damage numbers.
/// </summary>
public class AbilityCallout : MonoBehaviour
{
    public static void Show(Vector3 worldPos, string text, AbilityFeedback.Settings s)
    {
        var go = new GameObject("AbilityCallout");
        go.transform.position = worldPos;

        var tmp = go.AddComponent<TextMeshPro>();
        var font = TMP_Settings.defaultFontAsset;
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = s.fontSize;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.raycastTarget = false;
        tmp.color = s.calloutColor;
        // Outline via TMP's property so it recomputes the SDF scale ratios (setting the material
        // directly floods the glyph into a solid block — learned the hard way on damage numbers).
        tmp.outlineWidth = s.outlineWidth;
        tmp.outlineColor = s.outlineColor;

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.sortingOrder = 32001;   // above sprites and damage numbers

        // No per-character animation here. Text Animator's behaviours give every character its own
        // phase of one wave, so a callout came out as a scatter of letters rather than a word that
        // bobs: they are built for a line of dialogue read left to right at body-text size. What
        // moves instead is the label itself — see the punch in Update. Text Animator stays in the
        // project, unused.

        // A runtime-created 3D TMP won't build its mesh until forced — otherwise verts=0, nothing drawn.
        tmp.ForceMeshUpdate();

        go.AddComponent<AbilityCallout>().Init(tmp, s);
    }

    private AbilityFeedback.Settings _s;
    private TextMeshPro _tmp;
    private Color _baseColor;
    private float _age;

    private void Init(TextMeshPro tmp, AbilityFeedback.Settings s)
    {
        _tmp = tmp;
        _s = s;
        _baseColor = tmp.color;
        // Born big, so the first frame already reads as an arrival rather than a fade-in.
        if (Punching) transform.localScale = Vector3.one * s.punchScale;
    }

    private bool Punching => _s.punchScale > 1.001f && _s.punchSeconds > 0.0001f;

    /// <summary>
    /// Ease out with a small overshoot: the label passes its resting size, dips a hair under and
    /// settles. The dip is what makes it read as weight landing rather than a zoom.
    /// </summary>
    public static float EaseOutBack(float k)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float x = k - 1f;
        return 1f + c3 * x * x * x + c1 * x * x;
    }

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _s.lifetime)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += Vector3.up * _s.riseSpeed * Time.deltaTime;

        // The arrival: the whole label settles from too big to its size. Unclamped, so the easing's
        // overshoot is allowed to carry it a little under one before it comes back.
        if (Punching)
        {
            float k = Mathf.Clamp01(_age / _s.punchSeconds);
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(_s.punchScale, 1f, EaseOutBack(k));
        }

        // Fade the back half of the lifetime.
        float t = _age / _s.lifetime;
        if (t > 0.5f)
        {
            Color c = _baseColor;
            c.a = 1f - (t - 0.5f) / 0.5f;
            _tmp.color = c;
        }
    }
}

/// <summary>
/// An item's icon on its rarity's slot, rising over the unit whose ability it just fired: the same
/// arrival, rise and fade as <see cref="AbilityCallout"/>, drawn as the gear rather than as a word.
/// </summary>
public class AbilityIconCallout : MonoBehaviour
{
    /// <summary>Show <paramref name="item"/> at <paramref name="worldPos"/>. False when it has no icon to show.</summary>
    public static bool Show(Vector3 worldPos, Item item, AbilityFeedback.Settings s)
    {
        if (item == null || ItemCollection.Active == null) return false;
        var icon = ItemCollection.Active.GetItemIcon(new Item(item.Id));
        if (icon == null || icon.Sprite == null) return false;

        var go = new GameObject("AbilityIcon");
        go.transform.position = worldPos;
        var renderers = new List<SpriteRenderer>();

        // The slot first, so the gear reads as a piece of the bag rather than a loose sprite.
        var slot = Rarity.Background(item);
        if (slot != null) renderers.Add(Layer(go.transform, "Slot", slot, 32001, s.iconSize));
        renderers.Add(Layer(go.transform, "Icon", icon.Sprite, 32002, s.iconSize * 0.9f));

        go.AddComponent<AbilityIconCallout>().Init(renderers, s);
        return true;
    }

    private static SpriteRenderer Layer(Transform parent, string name, Sprite sprite, int order, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var r = go.AddComponent<SpriteRenderer>();
        r.sprite = sprite;
        r.sortingOrder = order;   // above sprites and damage numbers, like the name callout
        float extent = Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y);
        go.transform.localScale = Vector3.one * (extent > 0.0001f ? size / extent : 1f);
        return r;
    }

    private AbilityFeedback.Settings _s;
    private List<SpriteRenderer> _renderers;
    private float _age;

    private void Init(List<SpriteRenderer> renderers, AbilityFeedback.Settings s)
    {
        _renderers = renderers;
        _s = s;
        if (Punching) transform.localScale = Vector3.one * s.punchScale;
    }

    private bool Punching => _s.punchScale > 1.001f && _s.punchSeconds > 0.0001f;

    private void Update()
    {
        _age += Time.deltaTime;
        if (_age >= _s.lifetime) { Destroy(gameObject); return; }

        transform.position += Vector3.up * _s.riseSpeed * Time.deltaTime;

        if (Punching)
        {
            float k = Mathf.Clamp01(_age / _s.punchSeconds);
            transform.localScale = Vector3.one * Mathf.LerpUnclamped(_s.punchScale, 1f, AbilityCallout.EaseOutBack(k));
        }

        float t = _age / _s.lifetime;
        if (t > 0.5f)
            foreach (var r in _renderers)
                if (r != null) { var c = r.color; c.a = 1f - (t - 0.5f) / 0.5f; r.color = c; }
    }
}
