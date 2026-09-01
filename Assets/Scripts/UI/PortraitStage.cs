using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared plumbing for showing a HeroEditor figure inside the UI.
///
/// HeroEditor draws characters with SpriteRenderers, and a Screen Space - Camera canvas paints over
/// those regardless of sorting layer or order — so a figure can't simply be parented into a panel.
/// The way through is to park it on an off-screen "stage" that only a dedicated camera can see (via
/// a private layer), render that camera to a texture, and show the texture as a RawImage, which
/// composites like any other UI graphic.
///
/// Both <see cref="CharacterPreview"/> (the equipment doll) and <see cref="AvatarPortrait"/> (the
/// avatar cards) need exactly that, so the camera/texture/image setup lives here once. They still
/// differ in what they stage and how they frame it, which stays in each of them.
/// </summary>
public static class PortraitStage
{
    /// <summary>Stage positions are handed out far from gameplay and far from each other.</summary>
    private static readonly Vector3 Origin = new Vector3(1000f, 1000f, 0f);
    private const float Spacing = 500f;
    private static int _slots;

    /// <summary>
    /// Reserve a stage position no other portrait uses. Spacing is wide because HeroEditor rigs can
    /// be enormous in world units — cramped stages let one camera film another's subject.
    /// </summary>
    public static Vector3 ReserveSlot() => Origin + new Vector3(_slots++ * Spacing, 0f, 0f);

    /// <summary>
    /// Create a camera that films <paramref name="stage"/> on <paramref name="layer"/> only, into a
    /// fresh transparent RenderTexture. The layer is also removed from the main camera so the staged
    /// figure never shows up in the world.
    /// </summary>
    public static Camera CreateCamera(string name, Vector3 stage, int layer, int width, int height,
                                      float orthographicSize, out RenderTexture texture)
    {
        // 24 bits, not 16, because that is what carries a STENCIL buffer alongside the depth.
        // HeroEditor hides hair under a helmet with a SpriteMask, and sprite masks are drawn through
        // the stencil — on a depth-only target the mask silently does nothing, so every staged figure
        // wore its hair billowing out through the helmet while the same character looked right in the
        // world, which renders to the backbuffer and has a stencil.
        texture = new RenderTexture(width, height, 24) { name = name + "RT" };

        var go = new GameObject(name);
        go.transform.position = stage + new Vector3(0f, 0f, -10f);

        var cam = go.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = orthographicSize;
        cam.cullingMask = 1 << layer;              // the stage, and nothing else
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent, so the UI shows through
        cam.targetTexture = texture;

        if (Camera.main != null) Camera.main.cullingMask &= ~(1 << layer);

        return cam;
    }

    /// <summary>Add a RawImage showing <paramref name="texture"/>, centred on <paramref name="parent"/>.</summary>
    public static RawImage CreateImage(string name, RectTransform parent, Texture texture,
                                       Vector2 size, Vector2 offset)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var image = go.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;

        var rt = image.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;

        return image;
    }

    /// <summary>Put a whole rig on the stage layer so only its own camera renders it.</summary>
    public static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayerRecursive(child, layer);
    }
}
