using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Clears HideFlags.DontSave from the water2D_Icon texture both on editor load
/// and immediately before every build.
/// The texture lives inside a Resources folder so Unity includes it in the build,
/// but Water2D editor code can tag it DontSave at runtime, causing build error:
/// "An asset is marked with HideFlags.DontSave but is included in the build."
/// </summary>
[InitializeOnLoad]
public class Water2DIconBuildFix : IPreprocessBuildWithReport
{
    private const string SearchFolder = "Assets/Water2D_Tool";
    private const string AssetFilter  = "water2D_Icon t:Texture2D";

    public int callbackOrder => -100;

    // Clears the flag on every editor domain reload (handles the case where
    // Water2D editor code sets HideFlags on startup).
    static Water2DIconBuildFix()
    {
        // Defer to let other [InitializeOnLoad] code finish first.
        EditorApplication.delayCall += ClearIconHideFlags;
    }

    /// <summary>Strips HideFlags.DontSave before the build pipeline reads resource inclusion.</summary>
    public void OnPreprocessBuild(BuildReport report) => ClearIconHideFlags();

    private static void ClearIconHideFlags()
    {
        string[] guids = AssetDatabase.FindAssets(AssetFilter, new[] { SearchFolder });

        foreach (string guid in guids)
        {
            string    path = AssetDatabase.GUIDToAssetPath(guid);
            Texture2D tex  = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

            if (tex == null) continue;

            if ((tex.hideFlags & HideFlags.DontSave) != 0)
            {
                tex.hideFlags &= ~HideFlags.DontSave;
                Debug.Log($"[Water2DIconBuildFix] Cleared HideFlags.DontSave from '{path}'");
            }
        }
    }
}
