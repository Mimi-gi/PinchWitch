using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(ShadowColliderBuilder))]
public sealed class ShadowColliderBuilderEditor : Editor
{
  public override void OnInspectorGUI()
  {
    DrawDefaultInspector();

    EditorGUILayout.Space();
    EditorGUILayout.LabelField("Shadow Tools", EditorStyles.boldLabel);

    var builder = (ShadowColliderBuilder)target;

    using (new EditorGUILayout.HorizontalScope())
    {
      if (GUILayout.Button("Setup Shadow"))
      {
        builder.SetupShadow();
        EditorUtility.SetDirty(builder);
      }

      if (GUILayout.Button("Build Shadow Now"))
      {
        builder.BuildNow();
        EditorUtility.SetDirty(builder);
      }
    }

    if (GUILayout.Button("Setup Dirty Tracking"))
    {
      builder.SetupDirtyTracking();
      EditorUtility.SetDirty(builder);
    }

    if (GUILayout.Button("Create Shadow Only Proxy"))
    {
      builder.CreateShadowOnlyProxy();
      EditorUtility.SetDirty(builder);
    }
  }
}
