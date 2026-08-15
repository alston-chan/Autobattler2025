using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders an avatar card's head as a RenderTexture instead of leaving it as loose SpriteRenderers
/// inside the UI.
///
/// HeroEditor's <see cref="AvatarSetup"/> draws the face with SpriteRenderers while the card's own
/// backing and frame are UI Images. Once the canvas sorts above the world, that card art paints over
/// the face and no sorting layer or order rescues it — verified up to order 9000 and even with the
/// rig reparented out of the canvas. So the head is moved off to a private stage that only its own
/// camera can see, and the result is piped back into the card as a RawImage, which composites like
/// any other UI graphic. Same technique as <see cref="CharacterPreview"/>.
///
/// Each card needs its own camera and texture because all the cards are on screen at once (unlike
/// the equipment doll, where only one window is open and a single stage can be shared).
/// </summary>
public class AvatarPortrait : MonoBehaviour
{
    // The rig art is authored huge (the head spans ~512 units at unit scale, because the canvas was
    // scaling it down by ~200x), so normalise it. Stage positions come from PortraitStage, which
    // spaces them far apart — when they were only 20 units apart every camera filmed all three heads
    // stacked and each card showed the same face.
    private const float RigScale = 0.01f;

    private Camera _camera;
    private RenderTexture _texture;
    private Transform _rig;

    /// <summary>
    /// Move <paramref name="setup"/>'s rig onto the private stage, film it, and show the result on
    /// <paramref name="card"/>.
    /// </summary>
    public void Initialize(AvatarSetup setup, RectTransform card, int layer, int textureSize,
                           float cameraSize, Vector2 cameraOffset, float fillFraction)
    {
        if (setup == null || card == null) return;

        Vector3 stage = PortraitStage.ReserveSlot();

        // Detach from the canvas so the rig has a clean, known transform (parenting under a scaled
        // RectTransform is what made the sprites microscopic when they were moved naively).
        var rig = setup.transform;
        rig.SetParent(null, false);
        rig.position = stage;
        rig.localScale = Vector3.one * RigScale;
        rig.localRotation = Quaternion.identity;
        PortraitStage.SetLayerRecursive(rig, layer);
        _rig = rig;

        _camera = PortraitStage.CreateCamera("AvatarPortraitCamera", stage, layer,
                                             textureSize, textureSize, cameraSize, out _texture);

        AutoFrame(cameraSize, cameraOffset);

        float side = Mathf.Max(1f, card.rect.width * fillFraction);
        // Square, sized off the card's width — the texture is 1:1, so stretching to the card's
        // taller rect would squash the face.
        PortraitStage.CreateImage("AvatarPortraitImage", card, _texture,
                                  new Vector2(side, side), Vector2.zero);
    }

    /// <summary>
    /// Frame the camera on the head by measuring what actually renders. The rig's sprite bounds are
    /// useless for this — HeroEditor art carries huge transparent margins (the head measures 512
    /// units at unit scale), so fitting to bounds leaves the face as a speck. Instead: film wide
    /// once, find the opaque pixels, then refit to those. Portraits are built once at startup and
    /// never resize, so the one readback costs nothing and self-tunes to any art or helmet.
    /// <paramref name="fallbackSize"/> and <paramref name="fallbackOffset"/> apply only if nothing
    /// rendered.
    /// </summary>
    private void AutoFrame(float fallbackSize, Vector2 fallbackOffset)
    {
        Bounds bounds = default;
        bool any = false;
        foreach (var sr in GetRigRenderers())
        {
            if (!any) { bounds = sr.bounds; any = true; }
            else bounds.Encapsulate(sr.bounds);
        }
        if (!any) return;

        // Pass 1 — wide enough to guarantee the whole rig is in shot.
        Vector3 camPos = new Vector3(bounds.center.x, bounds.center.y, bounds.center.z - 10f);
        _camera.transform.position = camPos;
        _camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y) * 1.05f;
        _camera.Render();

        int w = _texture.width, h = _texture.height;
        var prevActive = RenderTexture.active;
        RenderTexture.active = _texture;
        var shot = new Texture2D(w, h, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        shot.Apply();
        RenderTexture.active = prevActive;

        var pixels = shot.GetPixels32();
        Destroy(shot);

        int minX = w, maxX = -1, minY = h, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (pixels[y * w + x].a <= 16) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < 0)   // nothing visible — keep the caller's guess rather than framing on nothing
        {
            _camera.orthographicSize = fallbackSize;
            _camera.transform.position = new Vector3(bounds.center.x + fallbackOffset.x,
                                                     bounds.center.y + fallbackOffset.y,
                                                     bounds.center.z - 10f);
            return;
        }

        // Pass 2 — refit to the opaque region, in world units.
        float unitsPerPixel = (2f * _camera.orthographicSize) / h;
        float worldMinX = camPos.x + (minX - w * 0.5f) * unitsPerPixel;
        float worldMaxX = camPos.x + (maxX - w * 0.5f) * unitsPerPixel;
        float worldMinY = camPos.y + (minY - h * 0.5f) * unitsPerPixel;
        float worldMaxY = camPos.y + (maxY - h * 0.5f) * unitsPerPixel;

        _camera.transform.position = new Vector3((worldMinX + worldMaxX) * 0.5f,
                                                 (worldMinY + worldMaxY) * 0.5f,
                                                 camPos.z);
        float halfHeight = (worldMaxY - worldMinY) * 0.5f;
        float halfWidth = (worldMaxX - worldMinX) * 0.5f;
        _camera.orthographicSize = Mathf.Max(0.01f, Mathf.Max(halfHeight, halfWidth) * 1.08f);
    }

    private SpriteRenderer[] GetRigRenderers() => _rig != null
        ? _rig.GetComponentsInChildren<SpriteRenderer>(false)
        : new SpriteRenderer[0];

    private void OnDestroy()
    {
        if (_camera != null) Destroy(_camera.gameObject);
        if (_texture != null) _texture.Release();
    }
}
