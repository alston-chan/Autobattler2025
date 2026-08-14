using System.Collections;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using UnityEngine;

/// <summary>
/// The death sequence: impact hold → fall → corpse → fade → despawn.
///
/// Previously <c>Health.Die()</c> set the death state and called <c>Destroy(gameObject)</c> on the
/// same frame, so the animation was never drawn even once — units vanished mid-swing. Death is the
/// punctuation mark of every fight, so it gets its own component and its own knobs, matching
/// <see cref="HitFeedback"/>.
///
/// Delaying the destroy means a corpse now lingers in <see cref="EntityRegistry"/> for a beat.
/// Everything that walks the registry must therefore skip <c>isDead</c> entities — see
/// <c>CombatAI.HandleAI</c>. Colliders are disabled here so corpses stop eating arrows.
/// </summary>
public class DeathFeedback : MonoBehaviour
{
    /// <summary>All the tunables. Lives on the shared CombatFeelSettings asset by default.</summary>
    [System.Serializable]
    public class Settings
    {
        [Header("Impact")]
        [Tooltip("Freeze-frame on the killing blow, applied to BOTH killer and victim. This is the " +
                 "beat that sells the kill — the victim holds its last living frame before dropping.")]
        public float killHitstop = 0.11f;
        [Tooltip("Blow the corpse out to a flat silhouette on the killing blow, then decay.")]
        public bool enableDeathFlash = true;
        public Color deathFlashColor = Color.white;
        [Tooltip("Longer than the normal hit flash — this one is meant to be read, not felt.")]
        public float deathFlashDuration = 0.18f;

        [Header("Fall")]
        public bool playDeathAnimation = true;
        [Tooltip("Fall AWAY from the killer: DeathB when struck from the front, DeathF when struck " +
                 "from behind. Characters only — monsters have a single Death state.")]
        public bool directionalFall = true;
        [Tooltip("World units the body slides away from the killer as it drops. 0 disables.\n\n" +
                 "Driven here rather than through Knockback, because Entity stops ticking Knockback " +
                 "the moment it is dead.")]
        public float lurchDistance = 0.4f;
        public float lurchDuration = 0.3f;

        [Header("Corpse")]
        [Tooltip("Seconds the corpse lies still before fading. Long enough to read the kill and to " +
                 "let the player see the board thin out; short enough not to clutter the field.")]
        public float corpseHold = 0.7f;
        public float fadeDuration = 0.45f;
        [Tooltip("Corpse sinks as it fades, so it reads as leaving the scene rather than blinking out.")]
        public float sinkDistance = 0.18f;
    }

    [Tooltip("Tick to ignore the global CombatFeelSettings asset and use the values below.")]
    public bool overrideGlobal = false;
    public Settings localSettings = new Settings();

    /// <summary>Live reference, so edits to the shared asset apply immediately — even mid-play.</summary>
    private Settings S => overrideGlobal ? localSettings : CombatFeelSettings.Active.death;

    private Entity _entity;
    private bool _running;

    public void Initialize(Entity entity) => _entity = entity;

    /// <summary>
    /// Run the whole death sequence and destroy the entity at the end. Called by
    /// <see cref="Health.Die"/>; safe to call more than once.
    /// </summary>
    public void PlayAndDespawn(Entity killer)
    {
        if (_running) return;
        _running = true;
        StartCoroutine(Sequence(killer));
    }

    private IEnumerator Sequence(Entity killer)
    {
        var s = S;

        // ── Leave combat immediately ────────────────────────────────────────────────
        // A dying unit must stop dealing damage. Its attack coroutines live on CombatAI and would
        // otherwise finish their wind-up and land a hit from beyond the grave.
        if (_entity.CombatAI != null) _entity.CombatAI.StopAllCoroutines();

        // Corpses must stop intercepting arrows (Projectile collides with Entity colliders).
        foreach (var col in GetComponentsInChildren<Collider2D>(true)) col.enabled = false;

        Animator animator = GetAnimator();
        // An interrupted attack may have left the animator running at attack speed.
        if (animator != null) animator.speed = 1f;

        // ── Impact ──────────────────────────────────────────────────────────────────
        if (s.enableDeathFlash && _entity.HitFeedback != null)
            _entity.HitFeedback.Flash(s.deathFlashColor, s.deathFlashDuration);

        if (s.killHitstop > 0f)
        {
            if (killer != null && !killer.isDead) killer.ApplyHitstop(s.killHitstop);
            _entity.ApplyHitstop(s.killHitstop);            // hold the last living frame
            yield return new WaitForSeconds(s.killHitstop); // Hitstop restores animator.speed itself
        }

        // ── Fall ────────────────────────────────────────────────────────────────────
        // +1 when the killer is to our right, so the body travels the other way.
        float awayFromKiller = 0f;
        if (killer != null)
        {
            float dx = killer.transform.position.x - transform.position.x;
            if (!Mathf.Approximately(dx, 0f)) awayFromKiller = -Mathf.Sign(dx);
        }

        if (s.playDeathAnimation) PlayDeathState(killer, s.directionalFall);

        if (s.lurchDistance > 0f && s.lurchDuration > 0f && awayFromKiller != 0f)
            yield return Lurch(awayFromKiller * s.lurchDistance, s.lurchDuration);

        // ── Corpse ──────────────────────────────────────────────────────────────────
        if (s.corpseHold > 0f) yield return new WaitForSeconds(s.corpseHold);

        yield return FadeOut(s.fadeDuration, s.sinkDistance);

        Destroy(gameObject);
    }

    /// <summary>
    /// Pick the fall direction. Units face their target, so a blow from the front drops them
    /// backwards (DeathB) and a blow from behind pitches them forwards (DeathF).
    /// </summary>
    private void PlayDeathState(Entity killer, bool directional)
    {
        if (_entity.character != null)
        {
            var state = CharacterState.DeathB;

            if (directional && killer != null)
            {
                // CombatAI.FaceTarget encodes facing in localScale.x, inverted for monsters.
                float facing = Mathf.Sign(transform.localScale.x) * (_entity.isCharacter ? 1f : -1f);
                float toKiller = Mathf.Sign(killer.transform.position.x - transform.position.x);
                if (!Mathf.Approximately(toKiller, 0f) && !Mathf.Approximately(facing, toKiller))
                    state = CharacterState.DeathF;   // struck from behind
            }

            _entity.character.SetState(state);
        }
        else if (_entity.monster != null)
        {
            _entity.monster.Die();
        }
    }

    private IEnumerator Lurch(float distance, float duration)
    {
        Vector3 start = transform.position;
        Vector3 end = start + Vector3.right * distance;

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            // Ease out — all the travel up front, like a body carrying the blow's momentum.
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / duration), 3f);
            transform.position = Vector3.Lerp(start, end, k);
            yield return null;
        }
        transform.position = end;
    }

    /// <summary>
    /// Fade every sprite to transparent while the body sinks.
    ///
    /// Alpha rides on SpriteRenderer.color, which the Sprites/Flash shader multiplies through
    /// (and which the stock sprite shader honours too), so this works whether or not HitFeedback
    /// has already swapped this unit onto the flash material.
    /// </summary>
    private IEnumerator FadeOut(float duration, float sinkDistance)
    {
        var renderers = GetComponentsInChildren<SpriteRenderer>(true);
        var colors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) colors[i] = renderers[i].color;

        Vector3 start = transform.position;
        Vector3 end = start + Vector3.down * sinkDistance;

        if (duration > 0f)
        {
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float k = Mathf.Clamp01(t / duration);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null) continue;
                    var c = colors[i];
                    c.a *= 1f - k;
                    renderers[i].color = c;
                }
                transform.position = Vector3.Lerp(start, end, k);
                yield return null;
            }
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            var c = colors[i];
            c.a = 0f;
            renderers[i].color = c;
        }
    }

    private Animator GetAnimator()
    {
        if (_entity.isCharacter && _entity.character != null) return _entity.character.Animator;
        if (_entity.monster != null) return _entity.monster.Animator;
        return null;
    }
}
