using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourceBar : MonoBehaviour
{
    [SerializeField] private Transform bar;
    [SerializeField] private SpriteRenderer barSR;

    public void SetSize(float sizeNormalized)
    {
        bar.localScale = new Vector3(sizeNormalized, 1f);
    }

    public void SetColor(Color color)
    {
        barSR.color = color;
    }
}
