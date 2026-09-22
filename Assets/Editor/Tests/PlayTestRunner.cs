using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Runs <see cref="PlayChecks"/> against a real play session and writes what happened to a file.
///
/// It is its own driver rather than the test runner's, because the test runner will not lend an
/// edit-mode test play mode unless the test lives in an asmdef assembly, and an asmdef assembly
/// cannot see <c>Assembly-CSharp</c> where the game is (see <see cref="PlayHarness"/>).
///
/// Two halves, because entering play mode reloads the domain and takes every static with it:
/// <see cref="Run"/> leaves a note in EditorPrefs and asks for play mode; <see cref="AfterReload"/>
/// runs on the other side, finds the note, and drives the checks from
/// <c>EditorApplication.update</c>. That is the same shape every hand-written probe in this project
/// arrived at, for the same reason.
///
/// It reports to <see cref="ResultsPath"/> rather than to its caller: a whole fight is minutes, and
/// the MCP tool that runs the ordinary suite gives up after sixty seconds. Start it, then poll.
/// </summary>
[InitializeOnLoad]
public static class PlayTestRunner
{
    /// <summary>Where the run writes its verdict. Under Temp, which is not version-controlled.</summary>
    public const string ResultsPath = "Temp/PlayTests.txt";

    private const string PendingKey = "Autobattler.PlayChecksPending";

    static PlayTestRunner() => EditorApplication.delayCall += AfterReload;

    [MenuItem("Tools/Tests/Run Play Tests")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[PlayChecks] Stop play mode first — the run starts its own session.");
            return;
        }

        if (EditorSceneManager.GetActiveScene().path != PlayHarness.Scene)
        {
            // A run against whatever scene happened to be open would pass by finding nothing.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(PlayHarness.Scene);
        }

        Write("PLAY running, started " + DateTime.Now.ToString("HH:mm:ss") + "\n");
        EditorPrefs.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    /// <summary>The far side of the domain reload: if a run was asked for, drive it.</summary>
    private static void AfterReload()
    {
        if (!EditorPrefs.GetBool(PendingKey, false)) return;
        if (!EditorApplication.isPlaying) return;      // the reload on the way out, not the way in

        EditorPrefs.SetBool(PendingKey, false);
        Drive(PlayChecks.All());
    }

    private static void Drive(System.Collections.Generic.List<PlayCheck> checks)
    {
        var report = new StringBuilder();
        var whole = Stopwatch.StartNew();
        int index = -1, passed = 0, failed = 0;
        Stopwatch one = null;

        // A coroutine stack, because a check yields other coroutines and something has to step into
        // them. Driving only the outermost one ran every check to its end in a tenth of a second and
        // reported four passes, which is the most dangerous thing a harness can do.
        var stack = new System.Collections.Generic.Stack<IEnumerator>();

        EditorApplication.CallbackFunction step = null;
        step = () =>
        {
            try
            {
                // Play mode ended under us — someone pressed stop, or a check crashed the session.
                if (!EditorApplication.isPlaying && index >= 0)
                {
                    report.Append("PLAY ABANDONED  play mode stopped during '")
                          .Append(index < checks.Count ? checks[index].Name : "?").Append("'\n");
                    Finish(step, report, whole, passed, ++failed);
                    return;
                }

                if (stack.Count == 0)
                {
                    index++;
                    if (index >= checks.Count) { Finish(step, report, whole, passed, failed); return; }
                    one = Stopwatch.StartNew();
                    stack.Push(checks[index].Body());
                }

                var top = stack.Peek();
                if (top.MoveNext())
                {
                    if (top.Current is IEnumerator nested) stack.Push(nested);
                    return;
                }

                stack.Pop();
                if (stack.Count > 0) return;          // an inner coroutine ended; the outer one goes on

                report.Append("PLAY PASSED  ").Append(checks[index].Name)
                      .Append("  ").Append((one.ElapsedMilliseconds / 1000f).ToString("0.0")).AppendLine("s");
                passed++;
            }
            catch (Exception ex)
            {
                report.Append("PLAY FAILED  ").Append(index >= 0 && index < checks.Count ? checks[index].Name : "(starting up)")
                      .Append("  ").Append(one != null ? (one.ElapsedMilliseconds / 1000f).ToString("0.0") : "0").AppendLine("s")
                      .Append("    ").AppendLine(ex.Message.Trim().Replace(Environment.NewLine, " "));
                failed++;
                stack.Clear();
            }
        };

        EditorApplication.update += step;
    }

    private static void Finish(EditorApplication.CallbackFunction step, StringBuilder report,
                               Stopwatch whole, int passed, int failed)
    {
        EditorApplication.update -= step;
        report.Append("PLAY done: passed=").Append(passed).Append(" failed=").Append(failed)
              .Append(" in ").Append((whole.ElapsedMilliseconds / 1000f).ToString("0")).Append("s\n");
        Write(report.ToString());
        Debug.Log("[PlayChecks] " + passed + " passed, " + failed + " failed — " + ResultsPath);
        if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
    }

    private static void Write(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResultsPath) ?? "Temp");
        File.WriteAllText(ResultsPath, text);
    }
}
