#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

public class BuildPlatformConfig : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    private static RenderPipelineAsset _savedAsset;

    public void OnPreprocessBuild(BuildReport report)
    {
        _savedAsset = GraphicsSettings.defaultRenderPipeline;

        if (report.summary.platform == BuildTarget.Android)
        {
            var mobileAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(
                AssetDatabase.GUIDToAssetPath("5e6cbd92b5a994a44ad05e5e2a5b25f2"));
            if (mobileAsset != null)
                GraphicsSettings.defaultRenderPipeline = mobileAsset;
            else
                Debug.LogWarning("[BuildPlatformConfig] Mobile_RPAsset tidak ditemukan via GUID.");
        }
        else
        {
            var pcAsset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(
                AssetDatabase.GUIDToAssetPath("4b83569db8fd64b4ab52f04c4b90a8f6"));
            if (pcAsset != null)
                GraphicsSettings.defaultRenderPipeline = pcAsset;
        }
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (_savedAsset != null)
            GraphicsSettings.defaultRenderPipeline = _savedAsset;
    }
}
#endif
