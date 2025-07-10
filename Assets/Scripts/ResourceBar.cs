using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourceBar : MonoBehaviour
{
    public Entity entity;

    [SerializeField] private Transform bar;
    [SerializeField] private SpriteRenderer barSR;

    private void LateUpdate()
    {
        // Always follow the entity at the specified offset and keep upright
        if (entity != null)
        {
            // TODO: healthBarOffset should be generalized
            transform.position = entity.transform.position + entity.healthBarOffset;
        }
    }

    public void SetSize(float sizeNormalized)
    {
        bar.localScale = new Vector3(sizeNormalized, 1f);
    }

    public void SetColor(Color color)
    {
        barSR.color = color;
    }
}
