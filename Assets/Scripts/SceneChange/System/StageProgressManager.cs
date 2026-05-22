using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class StageProgressManager
{
  private const string ClearStageCountKey = "ClearStageCount";
  private const string StageScenePrefix = "Stage_";
  private const int DefaultTotalStageCount = 10;

  private static int _totalStageCount = DefaultTotalStageCount;
  private static int _clearStageCount;
  private static int _lastNewlyClearedStageNum;
  private static bool _isLoaded;

  public static int TotalStageCount => _totalStageCount;

  public static int ClearStageCount
  {
    get
    {
      EnsureLoaded();
      return _clearStageCount;
    }
  }

  public static int NextPlayableStageNum
  {
    get
    {
      EnsureLoaded();
      return Mathf.Clamp(_clearStageCount + 1, 1, _totalStageCount);
    }
  }

  public static int LoadClearStageCount(int totalStageCount)
  {
    _totalStageCount = Mathf.Max(1, totalStageCount);
    _clearStageCount = Mathf.Clamp(PlayerPrefs.GetInt(ClearStageCountKey, 0), 0, _totalStageCount);
    _isLoaded = true;
    GameSceneManager.ClearStageCount = _clearStageCount;
    return _clearStageCount;
  }

  public static bool IsCleared(int stageNum)
  {
    EnsureLoaded();
    return stageNum >= 1 && stageNum <= _clearStageCount;
  }

  public static int RecordCurrentStageClear()
  {
    if (!TryGetCurrentStageNum(out int stageNum))
    {
      Debug.LogWarning($"[StageProgressManager] 現在のシーン '{SceneManager.GetActiveScene().name}' からステージ番号を取得できません。");
      return ClearStageCount;
    }

    return RecordClear(stageNum);
  }

  public static int RecordClear(int stageNum)
  {
    EnsureLoaded();

    if (stageNum < 1)
    {
      Debug.LogWarning($"[StageProgressManager] 無効なステージ番号です: {stageNum}");
      return _clearStageCount;
    }

    if (stageNum > _totalStageCount)
    {
      Debug.LogWarning($"[StageProgressManager] ステージ番号 {stageNum} は総ステージ数 {_totalStageCount} を超えています。");
      return _clearStageCount;
    }

    int clearedStageNum = stageNum;
    GameSceneManager.CurrentStageNum = clearedStageNum;

    if (clearedStageNum <= _clearStageCount)
    {
      GameSceneManager.ClearStageCount = _clearStageCount;
      return _clearStageCount;
    }

    _clearStageCount = clearedStageNum;
    _lastNewlyClearedStageNum = clearedStageNum;
    GameSceneManager.ClearStageCount = _clearStageCount;
    PlayerPrefs.SetInt(ClearStageCountKey, _clearStageCount);
    PlayerPrefs.Save();
    return _clearStageCount;
  }

  public static int ConsumeLastNewlyClearedStageNum()
  {
    int stageNum = _lastNewlyClearedStageNum;
    _lastNewlyClearedStageNum = 0;
    return stageNum;
  }

  public static void DeleteSaveData()
  {
    _clearStageCount = 0;
    _lastNewlyClearedStageNum = 0;
    _isLoaded = true;
    GameSceneManager.ClearStageCount = 0;
    GameSceneManager.CurrentStageNum = 1;
    PlayerPrefs.DeleteKey(ClearStageCountKey);
    PlayerPrefs.Save();
  }

  public static bool TryGetCurrentStageNum(out int stageNum)
  {
    if (TryParseStageSceneName(SceneManager.GetActiveScene().name, out stageNum))
    {
      return true;
    }

    stageNum = GameSceneManager.CurrentStageNum;
    return stageNum >= 1;
  }

  private static void EnsureLoaded()
  {
    if (_isLoaded)
    {
      return;
    }

    LoadClearStageCount(GameSceneManager.TotalStageCount > 0 ? GameSceneManager.TotalStageCount : DefaultTotalStageCount);
  }

  private static bool TryParseStageSceneName(string sceneName, out int stageNum)
  {
    stageNum = 0;

    if (string.IsNullOrEmpty(sceneName)
        || !sceneName.StartsWith(StageScenePrefix, StringComparison.OrdinalIgnoreCase))
    {
      return false;
    }

    string stageNumText = sceneName.Substring(StageScenePrefix.Length);
    return int.TryParse(stageNumText, out stageNum) && stageNum >= 1;
  }
}
