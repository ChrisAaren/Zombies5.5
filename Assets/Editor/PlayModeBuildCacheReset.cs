using UnityEditor;
using UnityEditor.Rendering;

namespace Zombies55.EditorTools
{
    // The SRP build-data singleton (CoreBuildData) lazily caches the list of quality-level pipeline assets and is only cleared after a build.
    // If anything touches it during Play mode it caches GraphicsApplier's runtime clone; with Reload Domain disabled that cache survives
    // Play mode, and the next player build then throws "UniversalRenderPipelineAsset has been destroyed" in URPPreprocessBuild.
    // Dropping the cache whenever Play mode ends makes the next build re-read the project's own assets.
    [InitializeOnLoad]
    static class PlayModeBuildCacheReset
    {
        static PlayModeBuildCacheReset()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void OnPlayMode(PlayModeStateChange c)
        {
            if (c == PlayModeStateChange.EnteredEditMode) CoreBuildData.instance.Dispose();
        }
    }
}
