using System;
using TMPro;
using UnityEngine;

/// <summary>
/// The "loud" feedback for cost abilities (ults). Per the readability rule (Docs/Juice.md), rare
/// events must dominate — so an ult announces itself with a floating name callout, a punctuating
/// flash on the caster, and a beefier hitstop on each victim.
///
/// Every knob lives on the shared <see cref="CombatFeelSettings"/> asset, so the whole thing can be
/// A/B tested (and, being a ScriptableObject, tuned live in Play mode) like all other combat feel.
/// </summary>
public static class AbilityFeedback
{
    [Serializable]
    public class Settings
    {
        [Header("Name callout")]
        [Tooltip("Float the ability's name above the caster — the attribution that tells the player " +
                 "their build just did something.")]
        public bool enableCallout = true;
        public Color calloutColor = new Color(1f, 0.88f, 0.3f, 1f);
        public float fontSize = 5f;
        public Vector3 offset = new Vector3(0f, 2.4f, 0f);
        public float riseSpeed = 1.1f;
        public float lifetime = 1.1f;
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

    /// <summary>Fire the on-cast feedback: caster flash + floating name callout.</summary>
    public static void Announce(Entity caster, string abilityName)
    {
        if (caster == null) return;
        var s = S;

        if (s.flashCaster && caster.HitFeedback != null)
            caster.HitFeedback.Flash(s.flashColor, s.flashDuration);

        if (s.enableCallout && !string.IsNullOrEmpty(abilityName))
            AbilityCallout.Show(caster.transform.position + s.offset, abilityName, s);
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
