using System.Collections;
using UnityEngine;

public abstract class Spell : ScriptableObject
{
    public string spellName;
    public float cooldown;
    [Tooltip("The effective range of this spell (used for AI and targeting)")]
    public float range = 1.5f;
    public abstract bool CanCast(Entity caster, Entity target);
    public abstract IEnumerator Cast(Entity caster, Entity target);
}
