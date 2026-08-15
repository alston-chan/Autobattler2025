using System.Collections.Generic;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Elements;
using Assets.HeroEditor.InventorySystem.Scripts.Helpers;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows a live doll of the character inside its equipment window.
///
/// The battlefield character can't simply be filmed — its neighbours and the background would be in
/// shot. Instead each window owns a cosmetic clone of the plain HeroEditor body (no Entity/CombatAI,
/// so it has no gameplay side effects) parked on an off-screen "stage" that only a dedicated camera
/// can see, via a private layer. That camera renders to a RenderTexture displayed by a RawImage in
/// the window, so the doll composites cleanly into the UI.
///
/// The clone mirrors <see cref="Equipment"/> on every refresh, so equipping updates the doll. The
/// real battlefield character keeps updating through Equipment.Preview as before — this is additive.
/// Only one window is open at a time, so a single stage/camera/texture is shared and each window
/// activates its own doll when it opens.
/// </summary>
public class CharacterPreview : MonoBehaviour
{
    [Tooltip("Layer used for the off-screen preview stage. Must be excluded from the main camera's " +
             "culling mask so the doll is never visible in the world.")]
    public int previewLayer = 8;

    [Header("Framing")]
    // Deliberately FIXED rather than auto-fitted to the doll's bounds. Fitting per refresh would
    // rescale the character every time a weapon changed its silhouette, which reads as the doll
    // jumping around; a stable frame is calmer. Defaults measured from the rendered opaque bounds of
    // a fully-equipped character (roughly x[-1.4,1.5], y[-0.2,3.0]).
    [Tooltip("Orthographic size of the preview camera — smaller crops in tighter on the character.")]
    public float cameraSize = 2.4f;
    [Tooltip("Camera centre relative to the doll's feet. Y ~1.4 centres a standing character.")]
    public Vector2 cameraOffset = new Vector2(0.05f, 1.4f);

    [Header("Placement in the window")]
    [Tooltip("Sibling panel to host the doll. 'HeroStats' has a large empty area under its stat lines; " +
             "the Equipment panel is used as a fallback if this isn't found.")]
    public string hostPanelName = "HeroStats";
    [Tooltip("Keep the 2:3 ratio of the render texture (256x384) or the doll will look stretched.")]
    public Vector2 imageSize = new Vector2(220f, 330f);
    [Tooltip("Anchored position within the host panel — negative Y drops it below the stat lines.")]
    public Vector2 imageOffset = new Vector2(0f, -80f);

    // One shared stage for every preview: only one window is open at a time.
    private static Camera _stageCamera;
    private static RenderTexture _stageTexture;
    private static readonly Vector3 StagePosition = new Vector3(1000f, 1000f, 0f);
    private static readonly List<GameObject> AllDolls = new List<GameObject>();

    private Equipment _equipment;
    private Character _doll;
    private GameObject _dollObject;
    private RawImage _image;
    private Appearance _sourceAppearance;

    /// <summary>
    /// Build the doll and its window image. <paramref name="bodyPrefab"/> is the plain HeroEditor
    /// character prefab (no gameplay scripts) used as the cosmetic body, and
    /// <paramref name="sourceAppearance"/> is the real character's look (hair, eyes, beard, skin)
    /// so the doll is recognisably that hero rather than the prefab's default face.
    /// </summary>
    public void Initialize(Equipment equipment, GameObject bodyPrefab, Appearance sourceAppearance)
    {
        _equipment = equipment;
        _sourceAppearance = sourceAppearance;
        if (_equipment == null || bodyPrefab == null) return;

        EnsureStage();
        CreateDoll(bodyPrefab);
        CreateImage();

        // Mirror equipment onto the doll whenever the window rebuilds (equip / unequip / refresh).
        _equipment.OnRefresh += Sync;
        Sync();
    }

    private void OnDestroy()
    {
        if (_equipment != null) _equipment.OnRefresh -= Sync;
        if (_dollObject != null)
        {
            AllDolls.Remove(_dollObject);
            Destroy(_dollObject);
        }
    }

    /// <summary>Create the shared camera + texture the first time any preview needs them.</summary>
    private void EnsureStage()
    {
        if (_stageCamera != null) return;

        _stageTexture = new RenderTexture(256, 384, 16) { name = "CharacterPreviewRT" };

        var camObject = new GameObject("CharacterPreviewCamera");
        DontDestroyOnLoad(camObject);
        _stageCamera = camObject.AddComponent<Camera>();
        _stageCamera.orthographic = true;
        _stageCamera.cullingMask = 1 << previewLayer;   // the stage, and nothing else
        _stageCamera.clearFlags = CameraClearFlags.SolidColor;
        _stageCamera.backgroundColor = new Color(0.08f, 0.08f, 0.10f, 0f);   // transparent backdrop
        _stageCamera.orthographicSize = cameraSize;
        _stageCamera.targetTexture = _stageTexture;
        camObject.transform.position = StagePosition
                                       + new Vector3(cameraOffset.x, cameraOffset.y, -10f);

        // The world camera must never see the stage.
        if (Camera.main != null) Camera.main.cullingMask &= ~(1 << previewLayer);
    }

    /// <summary>Clone the cosmetic body onto the stage, on the preview-only layer.</summary>
    private void CreateDoll(GameObject bodyPrefab)
    {
        _dollObject = Instantiate(bodyPrefab, StagePosition, Quaternion.identity);
        _dollObject.name = "PreviewDoll (" + name + ")";
        SetLayerRecursive(_dollObject.transform, previewLayer);

        _doll = _dollObject.GetComponent<Character>();
        AllDolls.Add(_dollObject);

        // Inactive until this window is opened; OnEnable makes it the visible one.
        _dollObject.SetActive(false);
    }

    /// <summary>Add the RawImage that displays the stage texture inside the equipment window.</summary>
    private void CreateImage()
    {
        // Prefer a dedicated sibling panel (HeroStats has room to spare); fall back to the Equipment
        // panel. Parenting to Equipment alone put the doll in the seam between panels.
        Transform host = _equipment.transform;
        var window = _equipment.transform.parent;
        if (window != null && !string.IsNullOrEmpty(hostPanelName))
        {
            var panel = window.Find(hostPanelName);
            if (panel != null) host = panel;
        }

        var go = new GameObject("CharacterPreviewImage", typeof(RectTransform));
        go.transform.SetParent(host, false);

        _image = go.AddComponent<RawImage>();
        _image.texture = _stageTexture;
        _image.raycastTarget = false;

        var rt = _image.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = imageSize;
        rt.anchoredPosition = imageOffset;
    }

    private void OnEnable()
    {
        // Windows are toggled with the number keys; whichever is open owns the shared stage.
        if (_dollObject == null) return;
        for (int i = 0; i < AllDolls.Count; i++)
            if (AllDolls[i] != null) AllDolls[i].SetActive(AllDolls[i] == _dollObject);

        Sync();
    }

    /// <summary>Re-apply the window's equipped items to the doll so it matches what's equipped.</summary>
    private void Sync()
    {
        if (_doll == null || _equipment == null) return;

        // Face and body first (hair, eyes, beard, skin tone), THEN equipment on top — otherwise the
        // appearance pass would overwrite the gear. Without this the doll wears the right kit but
        // shows the prefab's default face, so every hero looks the same.
        if (_sourceAppearance != null && _sourceAppearance.CharacterAppearance != null)
            _sourceAppearance.CharacterAppearance.Setup(_doll);

        CharacterInventorySetup.Setup(_doll, new List<Item>(_equipment.Items));
        _doll.Initialize();
    }

    private static void SetLayerRecursive(Transform t, int layer)
    {
        t.gameObject.layer = layer;
        foreach (Transform child in t) SetLayerRecursive(child, layer);
    }
}
