using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class URPMigrationBatch
{
    [MenuItem("Tools/URP/Run Material Upgrade")]
    public static void Run()
    {
        try
        {
            Debug.Log("[URPMigrationBatch] Starting URP migration.");
            EnsureUrpAssigned();
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
            Debug.LogError($"[URPMigrationBatch] Migration failed: {ex}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }

    static UniversalRenderPipelineAsset EnsureUrpAssigned()
    {
        var current = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        if (current != null)
        {
            return current;
        }

        var urpAsset = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>)
            .FirstOrDefault(asset => asset != null);

        if (urpAsset == null)
        {
            throw new InvalidOperationException("No UniversalRenderPipelineAsset was found in project.");
        }

        GraphicsSettings.defaultRenderPipeline = urpAsset;

        var currentQuality = QualitySettings.GetQualityLevel();
        var qualityNames = QualitySettings.names;
        for (var i = 0; i < qualityNames.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            if (QualitySettings.renderPipeline == null)
            {
                QualitySettings.renderPipeline = urpAsset;
            }
        }
        QualitySettings.SetQualityLevel(currentQuality, false);
        if (QualitySettings.renderPipeline == null)
        {
            QualitySettings.renderPipeline = urpAsset;
        }

        return urpAsset;
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
