/// <summary>
/// R キーなどのランタイムリセット時に、初期状態へ戻せるコンポーネント用インターフェース。
/// ResetOrder が小さいものから順番に実行される。
/// </summary>
public interface IRuntimeResettable
{
  int ResetOrder { get; }

  void ResetRuntimeState();
}
