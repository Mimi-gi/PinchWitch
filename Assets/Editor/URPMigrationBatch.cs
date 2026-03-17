using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class URPMigrationBatch
{
    [MenuItem("Tools/URP/Run Built-in To URP Conversion")]
    public static void Run()
    {
        try
        {
            Debug.Log("[URPMigrationBatch] Starting URP migration.");

            var urpAsset = EnsureUrpAsset();
            AssignPipelineToGraphicsAndQuality(urpAsset);
            UpgradeMaterialsToUrp();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[URPMigrationBatch] Migration completed.");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[URPMigrationBatch] Conversion failed: {ex}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }

    static UniversalRenderPipelineAsset EnsureUrpAsset()
    {
        const string settingsFolder = "Assets/Settings/Rendering";
        const string rendererPath = settingsFolder + "/DefaultUniversalRenderer.asset";
        const string pipelinePath = settingsFolder + "/UniversalRenderPipeline.asset";

        var existing = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>)
            .FirstOrDefault(asset => asset != null);
        if (existing != null)
        {
            Debug.Log($"[URPMigrationBatch] Reusing existing URP Asset: {AssetDatabase.GetAssetPath(existing)}");
            return existing;
        }

        Directory.CreateDirectory(settingsFolder);

        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, rendererPath);

        var urpAsset = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(urpAsset, pipelinePath);

        Debug.Log($"[URPMigrationBatch] Created URP Asset: {pipelinePath}");
        return urpAsset;
    }

    static void AssignPipelineToGraphicsAndQuality(UniversalRenderPipelineAsset urpAsset)
    {
        GraphicsSettings.defaultRenderPipeline = urpAsset;

        var currentQuality = QualitySettings.GetQualityLevel();
        var qualityNames = QualitySettings.names;
        for (var i = 0; i < qualityNames.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urpAsset;
        }
        QualitySettings.SetQualityLevel(currentQuality, false);
        QualitySettings.renderPipeline = urpAsset;

        Debug.Log("[URPMigrationBatch] Assigned URP Asset to GraphicsSettings and all Quality levels.");
    }

    static void UpgradeMaterialsToUrp()
    {
        var upgraders = MaterialUpgrader.FetchAllUpgradersForPipeline(typeof(UniversalRenderPipelineAsset));
        var materials = MaterialUpgrader.FetchAllUpgradableMaterialsForPipeline(typeof(UniversalRenderPipelineAsset));

        var upgraded = 0;
        foreach (var material in materials)
        {
            var message = string.Empty;
            var ok = MaterialUpgrader.Upgrade(
                material,
                upgraders,
                MaterialUpgrader.UpgradeFlags.LogMessageWhenNoUpgraderFound,
                ref message);
            if (ok)
            {
                upgraded++;
            }
            else if (!string.IsNullOrEmpty(message))
            {
                Debug.LogWarning($"[URPMigrationBatch] Material upgrade skipped: {material.name} ({message})");
            }
        }

        Debug.Log($"[URPMigrationBatch] Material upgrade finished. Upgraded {upgraded}/{materials.Count} materials.");
    }
}
