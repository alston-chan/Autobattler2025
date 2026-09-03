using UnityEngine;

/// <summary>
/// Keeps the background sized to the camera, whatever the art's pixel size or the view's aspect.
///
/// The scene used to scale the background by a hand-picked 0.5, which made a 4K painting at 100
/// pixels per unit 19.2 by 10.8 units against a camera that sees 17.8 by 10: about 7% of the art
/// off every edge, and a different amount the moment anyone changed the camera or the Game View's
/// aspect. This computes the scale from the camera each frame instead, in the editor too.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class BackgroundFit : MonoBehaviour
{
    public enum Mode
    {
        /// <summary>Show the whole painting; bars appear on the sides where the aspect differs.</summary>
        Contain,
        /// <summary>Fill the view; the painting is cropped on the sides where the aspect differs.</summary>
        Cover,
    }

    [Tooltip("Contain shows the whole painting and lets the view's bars show where the aspect differs; " +
             "Cover fills the view and crops. At 16:9 the paintings are 16:9 too, so the two agree.")]
    public Mode mode = Mode.Contain;

    [Tooltip("Follow the camera's position as well as its size, so the painting stays centred on it.")]
    public bool followCamera = true;

    private SpriteRenderer _renderer;
    private Camera _camera;

    private void OnEnable()
    {
        _renderer = GetComponent<SpriteRenderer>();
        Fit();
    }

    private void LateUpdate() => Fit();

    private void Fit()
    {
        if (_camera == null) _camera = Camera.main;
        if (_camera == null || _renderer == null || _renderer.sprite == null) return;

        // The painting's size at scale 1, in world units — from the sprite, so a change of pixels
        // per unit or of max texture size on import is absorbed here rather than in the scene.
        var sprite = _renderer.sprite;
        float artWidth = sprite.rect.width / sprite.pixelsPerUnit;
        float artHeight = sprite.rect.height / sprite.pixelsPerUnit;
        if (artWidth <= 0f || artHeight <= 0f) return;

        float viewHeight = _camera.orthographicSize * 2f;
        float viewWidth = viewHeight * _camera.aspect;

        float byWidth = viewWidth / artWidth;
        float byHeight = viewHeight / artHeight;
        float scale = mode == Mode.Contain ? Mathf.Min(byWidth, byHeight) : Mathf.Max(byWidth, byHeight);

        var current = transform.localScale;
        if (!Mathf.Approximately(current.x, scale) || !Mathf.Approximately(current.y, scale))
            transform.localScale = new Vector3(scale, scale, 1f);

        if (followCamera)
        {
            var at = _camera.transform.position;
            var here = transform.position;
            if (!Mathf.Approximately(here.x, at.x) || !Mathf.Approximately(here.y, at.y))
                transform.position = new Vector3(at.x, at.y, here.z);
        }
    }
}
