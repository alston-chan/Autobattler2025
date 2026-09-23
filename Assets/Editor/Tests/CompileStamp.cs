using System;
using System.IO;
using UnityEditor;

/// <summary>
/// Touches <c>Temp/CompileStamp.txt</c> every time the editor's domain loads, so a script outside
/// Unity can wait for a recompile to have landed instead of sleeping for one.
///
/// The dev loop (tools/dev.sh) used to sleep a flat 55 seconds after asking Hot Reload to
/// recompile, on every cycle, whether the compile took ten seconds or never started. Two files say
/// what actually happened: the editor assembly in Library/ScriptAssemblies is rewritten when the
/// compile finishes (it depends on everything, so any script change rebuilds it), and this stamp is
/// rewritten when the domain has reloaded with it.
/// </summary>
[InitializeOnLoad]
public static class CompileStamp
{
    public const string Path = "Temp/CompileStamp.txt";

    static CompileStamp()
    {
        try { File.WriteAllText(Path, DateTime.Now.ToString("HH:mm:ss.fff") + (EditorApplication.isPlayingOrWillChangePlaymode ? " play" : " edit") + "\n"); }
        catch (IOException) { }   // Temp is being rewritten under us; the next reload writes it
    }
}
