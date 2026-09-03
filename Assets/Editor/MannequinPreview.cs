using System.Collections.Generic;
using System.Linq;
using Assets.HeroEditor.Common.Scripts.CharacterScripts;
using Assets.HeroEditor.InventorySystem.Scripts;
using Assets.HeroEditor.InventorySystem.Scripts.Data;
using Assets.HeroEditor.InventorySystem.Scripts.Helpers;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A body to see equipment on, inside an editor window. The plain Human prefab — the same one the
/// runtime doll uses — instantiated into Unity's preview scene, dressed through the same setup the
/// game dresses heroes with, and rendered to a texture on demand. No open scene, no play mode.
///
/// The rig sets itself up in Start and OnEnable, which do not run in edit mode; the preview calls
/// what it needs by hand: OnEnable for the hair mask's sorting (so a helmet covers the hair rather
/// than the other way round) and Initialize after dressing. Camera framing is copied from the
/// runtime doll, which had already found the numbers.
/// </summary>
public class MannequinPreview : System.IDisposable
{
    public const string BodyPrefabPath = "Assets/HeroEditor/FantasyHeroes/Prefabs/Human.prefab";
    private const float CameraSize = 2.4f;
    private static readonly Vector2 CameraOffset = new Vector2(0.05f, 1.4f);

    private PreviewRenderUtility _preview;
    private GameObject _doll;
    private Character _character;
    private string _outfit;      // the ids last dressed, so a repaint does not re-dress

    /// <summary>Put these items on the body — all of them at once, replacing what it wore.</summary>
    public void Dress(IEnumerable<string> itemIds)
    {
        var ids = itemIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().OrderBy(id => id).ToList();
        string outfit = string.Join("|", ids);
        if (outfit == _outfit && _character != null) return;

        if (!Ensure()) return;
        if (ItemCollection.Active == null) ItemCollection.Active = Catalog.Items();

        var items = new List<Item>();
        foreach (var id in ids)
            if (Catalog.IsKnown(id)) items.Add(new Item(id));

        try
        {
            CharacterInventorySetup.Setup(_character, items);
            _character.Initialize();
            _outfit = outfit;
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[Mannequin] Could not dress {outfit}: {ex.Message}");
            _outfit = null;
        }
    }

    /// <summary>Draw the body as last dressed into this rectangle. Cheap enough to do every repaint.</summary>
    public void Draw(Rect rect)
    {
        if (_preview == null || _doll == null || Event.current.type != EventType.Repaint) return;
        _preview.BeginPreview(rect, GUIStyle.none);
        _preview.Render();
        var texture = _preview.EndPreview();
        GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
    }

    private bool Ensure()
    {
        if (_preview == null)
        {
            _preview = new PreviewRenderUtility();
            var camera = _preview.camera;
            camera.orthographic = true;
            camera.orthographicSize = CameraSize;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 50f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.transform.position = new Vector3(CameraOffset.x, CameraOffset.y, -10f);
            camera.transform.rotation = Quaternion.identity;
        }
        if (_doll == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefabPath);
            if (prefab == null) { Debug.LogWarning("[Mannequin] No body prefab at " + BodyPrefabPath); return false; }
            _doll = _preview.InstantiatePrefabInScene(prefab);
            _doll.transform.position = Vector3.zero;
            _character = _doll.GetComponentInChildren<Character>();
            if (_character == null) { Debug.LogWarning("[Mannequin] The body prefab has no Character."); return false; }
            _character.OnEnable();
            _outfit = null;
        }
        return true;
    }

    public void Dispose()
    {
        _preview?.Cleanup();
        _preview = null;
        _doll = null;
        _character = null;
        _outfit = null;
    }
}
