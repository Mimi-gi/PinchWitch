using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class StageSelectGrabTarget : MonoBehaviour
{
  [SerializeField] private int _stageNum = 1;

  public int StageNum => _stageNum;

  private void OnValidate()
  {
    _stageNum = Mathf.Max(1, _stageNum);
  }
}
