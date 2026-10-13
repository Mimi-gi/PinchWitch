// 敵の移動コンポーネント共通の口。スタンや光ギミックなど外部から速度を操作するときに使う。
public interface IEnemyMover
{
    /// <summary>
    /// 外部からの速度倍率。0 で移動停止、1 で通常速度。
    /// </summary>
    void SetExternalSpeedMultiplier(float multiplier);
}
