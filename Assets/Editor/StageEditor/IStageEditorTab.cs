using UnityEditor;

// ステージエディタのタブ 1 枚分。ウィンドウはアクティブなタブにだけ GUI とシーン操作を流す。
public interface IStageEditorTab
{
    string Title { get; }

    void OnEnable();

    void OnDisable();

    void OnGUI();

    void OnSceneGUI(SceneView sceneView);
}
