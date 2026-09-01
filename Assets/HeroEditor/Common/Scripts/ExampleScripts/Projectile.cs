using System.Collections.Generic;
using UnityEngine;

namespace Assets.HeroEditor.Common.Scripts.ExampleScripts
{
    /// <summary>
    /// General behaviour for projectiles: bullets, rockets and other.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
        public List<Renderer> Renderers;
        public GameObject Trail;
        public GameObject Impact;
        public Rigidbody2D Rigidbody;

        public float damage = 10f;

        // Added for this project: a shot that cannot crit while every melee swing can is a rule
        // nobody chose. Set by whichever spell fires this, rolled per hit in Bang below.
        public float critChance;

        public float knockbackForce = 3.5f;
        public float homingSpeed = 18.75f;

        public Entity shooter;
        public Entity target;

        private void Awake()
        {
            Rigidbody = GetComponent<Rigidbody2D>();
        }

        public void Start()
        {
            Destroy(gameObject, 5);
        }

        public void Update()
        {
            if (Rigidbody != null)
            {
                // A dead target counts as no target. Its colliders are switched off the moment it
                // dies (DeathFeedback, so corpses stop eating arrows), which means Bang can never
                // fire and destroy this — while homing kept steering onto the body. The projectile
                // sat on the corpse until its five-second self-destruct. Losing the target here lets
                // it carry on past on its last heading and leave, which is what a missed shot does.
                if (target != null && !target.isDead)
                {
                    Vector2 dir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
                    Rigidbody.velocity = homingSpeed * dir;
                    transform.right = Rigidbody.velocity.normalized;
                }
                else if (Rigidbody.velocity.sqrMagnitude > 0.01f)
                {
                    transform.right = Rigidbody.velocity.normalized;
                }
                else
                {
                    // No target left to steer at and no heading to coast on. Bang only fires on the
                    // target, so this can never hit anything either: it would hang exactly where it
                    // was spawned — in practice in the caster's hand — for the full five seconds
                    // before the self-destruct. Whoever launches a projectile owes it a velocity;
                    // when that hasn't happened there is nothing worth drawing.
                    Destroy(gameObject);
                }
            }
        }

        public void OnTriggerEnter2D(Collider2D other)
        {
            Bang(other.gameObject);
        }

        public void OnCollisionEnter2D(Collision2D other)
        {
            Bang(other.gameObject);
        }

        private void Bang(GameObject other)
        {
            Entity entity = other.GetComponent<Entity>();
            if (entity != null && target != null && entity == target)
            {
                entity.TakeDamage(damage, shooter, AttackRoll.IsCrit(critChance));
                Vector3 direction = (other.transform.position - transform.position).normalized;
                entity.ApplyKnockback(direction, knockbackForce);

                ReplaceImpactSound(other);
                Impact.SetActive(true);
                // Destroy(GetComponent<SpriteRenderer>());
                // Destroy(GetComponent<Rigidbody>());
                // Destroy(GetComponent<Collider>());
                Destroy(gameObject);

                // foreach (var ps in Trail.GetComponentsInChildren<ParticleSystem>())
                // {
                //     ps.Stop();
                // }

                // foreach (var tr in Trail.GetComponentsInChildren<TrailRenderer>())
                // {
                //     tr.enabled = false;
                // }
            }
        }

        private void ReplaceImpactSound(GameObject other)
        {
            var sound = other.GetComponent<AudioSource>();

            if (sound != null && sound.clip != null)
            {
                Impact.GetComponent<AudioSource>().clip = sound.clip;
            }
        }
    }
}