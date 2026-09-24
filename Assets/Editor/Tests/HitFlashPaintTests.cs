using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The hit flash moves every renderer on a unit onto the flash shader. HeroEditor's eyes and painted
/// equipment use its "Gray Paint" material, which colours only some pixels — the iris, never the eye
/// whites — so they must land on a flash material in paint mode carrying the same settings, or the
/// first hit of a fight tints every hero's eye whites the colour of their irises.
/// </summary>
public class HitFlashPaintTests
{
    [Test]
    public void AFlashKeepsHeroEditorsEyePaint()
    {
        var eyesPaint = AssetDatabase.LoadAssetAtPath<Material>("Assets/HeroEditor/Common/Shaders/EyesPaint.mat");
        Assert.That(eyesPaint, Is.Not.Null, "HeroEditor's EyesPaint material has moved");

        var unit = new GameObject("FlashPaintUnit");
        try
        {
            var feedback = unit.AddComponent<HitFeedback>();
            var body = new GameObject("Body").AddComponent<SpriteRenderer>();
            body.transform.SetParent(unit.transform);
            var eyes = new GameObject("Eyes").AddComponent<SpriteRenderer>();
            eyes.transform.SetParent(unit.transform);
            eyes.sharedMaterial = eyesPaint;

            feedback.Flash(Color.white, 0.1f);

            var m = eyes.sharedMaterial;
            Assert.That(m.shader.name, Is.EqualTo("Sprites/Flash"), "the eyes should flash with everything else");
            Assert.That(m.GetFloat("_PaintMode"), Is.EqualTo(1f), "the eyes lost their paint rule, so their whites take the eye colour");
            Assert.That(m.GetFloat("_Inverse"), Is.EqualTo(eyesPaint.GetFloat("_Inverse")));
            Assert.That(m.GetFloat("_ColorMultiplier"), Is.EqualTo(eyesPaint.GetFloat("_ColorMultiplier")));
            Assert.That(m.GetFloat("_SaturationBound"), Is.EqualTo(eyesPaint.GetFloat("_SaturationBound")));
            Assert.That(body.sharedMaterial.GetFloat("_PaintMode"), Is.EqualTo(0f), "an ordinary sprite keeps the plain flash");

            feedback.Flash(Color.white, 0.1f);
            Assert.That(eyes.sharedMaterial, Is.SameAs(m), "a second hit should reuse the paint flash, not stack new materials");
        }
        finally
        {
            Object.DestroyImmediate(unit);
        }
    }
}
