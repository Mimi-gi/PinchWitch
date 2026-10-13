using System;
using System.Collections.Generic;
using UnityEngine;

// ステージ番号 → StageData の対応表。Resources/StageCatalog.asset に置き、実行時は Load() で取得する。
// ここに登録されたステージ番号は、シーンではなく共通シーン(Stage_Runtime)で読み込まれる。
[CreateAssetMenu(menuName = "PinchWitch/Stage Catalog", fileName = "StageCatalog")]
public sealed class StageCatalog : ScriptableObject
{
    public const string ResourcesPath = "StageCatalog";

    [Serializable]
    public sealed class Entry
    {
        public int stageNumber;
        public StageData stage;
    }

    public List<Entry> entries = new List<Entry>();

    private static StageCatalog _cached;

    public static StageCatalog Load()
    {
        if (_cached == null)
        {
            _cached = Resources.Load<StageCatalog>(ResourcesPath);
        }

        return _cached;
    }

    public bool TryGetStage(int stageNumber, out StageData stage)
    {
        foreach (var entry in entries)
        {
            if (entry != null && entry.stageNumber == stageNumber && entry.stage != null)
            {
                stage = entry.stage;
                return true;
            }
        }

        stage = null;
        return false;
    }

    public void SetStage(int stageNumber, StageData stage)
    {
        foreach (var entry in entries)
        {
            if (entry != null && entry.stageNumber == stageNumber)
            {
                entry.stage = stage;
                return;
            }
        }

        entries.Add(new Entry { stageNumber = stageNumber, stage = stage });
        entries.Sort((left, right) => left.stageNumber.CompareTo(right.stageNumber));
    }
}
