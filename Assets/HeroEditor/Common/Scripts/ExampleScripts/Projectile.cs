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
                if (target != null)
                {
                    Vector2 dir = ((Vector2)target.transform.position - (Vector2)transform.position).normalized;
                    Rigidbody.velocity = homingSpeed * dir;
                    transform.right = Rigidbody.velocity.normalized;
                }
                else if (Rigidbody.velocity.sqrMagnitude > 0.01f)
                {
                    transform.right = Rigidbody.velocity.normalized;
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
                entity.TakeDamage(damage);
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