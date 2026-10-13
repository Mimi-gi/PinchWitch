using UnityEngine;

// 共通シーン(Stage_Runtime)に置く。現在のステージ番号の StageData を StageCatalog から取り出し、シーンを組み立てる。
// 影の初期生成(PlaneStageSetter)やプレイヤーの初期位置記録より先に動く必要があるので、実行順を早めている。
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public sealed class StageRuntimeLoader : MonoBehaviour
{
    [SerializeField] private StageSceneAnchors anchors;
    [Tooltip("設定するとステージ番号に関係なくこのステージを読み込む(シーン単体でのテスト用)。")]
    [SerializeField] private StageData overrideStage;

    public StageData LoadedStage { get; private set; }

    private void Awake()
    {
        if (anchors == null)
        {
            anchors = FindFirstObjectByType<StageSceneAnchors>(FindObjectsInactive.Include);
        }

        var stage = ResolveStage();
        if (stage == null || anchors == null)
        {
            Debug.LogError($"[StageRuntimeLoader] ステージ {GameSceneManager.CurrentStageNum} のデータが見つかりません。StageCatalog を確認してください。", this);
            return;
        }

        LoadedStage = stage;
        StageBuilder.BuildRuntime(stage, anchors);
    }

    private StageData ResolveStage()
    {
        if (overrideStage != null)
        {
            return overrideStage;
        }

        var catalog = StageCatalog.Load();
        if (catalog == null)
        {
            return null;
        }

        if (catalog.TryGetStage(GameSceneManager.CurrentStageNum, out var stage))
        {
            return stage;
        }

        // このシーンを直接再生した場合など、番号が対応しないときは先頭のステージを使う。
        foreach (var entry in catalog.entries)
        {
            if (entry?.stage != null)
            {
                return entry.stage;
            }
        }

        return null;
    }
}
