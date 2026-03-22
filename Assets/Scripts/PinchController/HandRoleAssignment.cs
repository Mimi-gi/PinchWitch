public struct HandRoleAssignment
{
  // 移動担当の手を保持する。
  public MediaPipeReceiver.TrackedHandType MovementHandType { get; }

  // 回転担当の手を保持する。
  public MediaPipeReceiver.TrackedHandType RotationHandType { get; }

  // 左右の役割割り当てを生成する。
  public HandRoleAssignment(MediaPipeReceiver.TrackedHandType movementHandType, MediaPipeReceiver.TrackedHandType rotationHandType)
  {
    MovementHandType = movementHandType;
    RotationHandType = rotationHandType;
  }

  // 左右入れ替え設定から役割割り当てを作る。
  public static HandRoleAssignment Create(bool swapLeftRightRoles)
  {
    return swapLeftRightRoles
      ? new HandRoleAssignment(MediaPipeReceiver.TrackedHandType.Left, MediaPipeReceiver.TrackedHandType.Right)
      : new HandRoleAssignment(MediaPipeReceiver.TrackedHandType.Right, MediaPipeReceiver.TrackedHandType.Left);
  }
}
