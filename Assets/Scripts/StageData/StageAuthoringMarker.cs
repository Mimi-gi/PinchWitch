using UnityEngine;

// ステージ作成用シーン(Stage_Authoring)に置く目印。
// テストプレイ中にゴールしてもクリア記録やシーン遷移をしないようにするために使う。
[DisallowMultipleComponent]
public sealed class StageAuthoringMarker : MonoBehaviour
{
    private static StageAuthoringMarker _active;

    public static bool IsAuthoringScene => _active != null;

    private void Awake()
    {
        _active = this;
    }

    private void OnDestroy()
    {
        if (_active == this)
        {
            _active = null;
        }
    }
}
