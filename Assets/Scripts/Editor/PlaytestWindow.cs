using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Playtest > Scenarios: pick the fight to play. Lists every PlaytestScenario asset with a
/// Use button, and Default for the game as authored. The choice lands in Resources/Playtest and
/// takes effect the next time Play starts.
/// </summary>
public class PlaytestWindow : EditorWindow
{
    [MenuItem("Tools/Playtest/Scenarios")]
    public static void Open() => GetWindow<PlaytestWindow>("Playtest");

    private Vector2 _scroll;

    private void OnGUI()
    {
        var playtest = Playtest.Active;
        if (playtest == null)
        {
            EditorGUILayout.HelpBox("No Resources/Playtest asset. Create one via Assets > Create > Data > Playtest.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField("In force", playtest.active != null ? playtest.active.name : "Default (the game as authored)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Takes effect when Play starts. Stop and start again to switch.", MessageType.Info);

        if (GUILayout.Button("Default")) Set(playtest, null);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        foreach (var guid in AssetDatabase.FindAssets("t:PlaytestScenario"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var scenario = AssetDatabase.LoadAssetAtPath<PlaytestScenario>(path);
            if (scenario == null) continue;

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            bool inForce = playtest.active == scenario;
            EditorGUILayout.LabelField((inForce ? "▶ " : "") + scenario.name, EditorStyles.boldLabel);
            if (GUILayout.Button("Use", GUILayout.Width(60))) Set(playtest, scenario);
            if (GUILayout.Button("Select", GUILayout.Width(60))) Selection.activeObject = scenario;
            EditorGUILayout.EndHorizontal();
            var summary = new List<string>();
            if (scenario.heroes != null) foreach (var h in scenario.heroes) if (h != null) summary.Add(h.heroName);
            EditorGUILayout.LabelField(summary.Count > 0 ? string.Join(", ", summary) : "the whole company", EditorStyles.miniLabel);
            if (scenario.encounter != null) EditorGUILayout.LabelField("vs " + scenario.encounter.encounterName + " (" + (scenario.encounter.spawns != null ? scenario.encounter.spawns.Count : 0) + ")", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(scenario.notes)) EditorGUILayout.LabelField(scenario.notes, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
    }

    private static void Set(Playtest playtest, PlaytestScenario scenario)
    {
        playtest.active = scenario;
        EditorUtility.SetDirty(playtest);
        AssetDatabase.SaveAssetIfDirty(playtest);
    }
}
