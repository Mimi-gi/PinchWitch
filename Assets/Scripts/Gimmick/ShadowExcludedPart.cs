using UnityEngine;

// 影足場を落とさない部品(ガラス等)に付けるマーカー。
// 付いた GameObject とその子孫のメッシュは ShadowColliderBuilder の投影対象から外れる。
// 光線を通すかどうかはレイヤー(Glass)で判定するため、ガラス部品は Glass レイヤーに置くこと。
[DisallowMultipleComponent]
public sealed class ShadowExcludedPart : MonoBehaviour
{
    /// <summary>
    /// target から stopAt(含む)までの親階層にマーカーがあれば true。
    /// stopAt が null の場合はシーンルートまで遡る。
    /// </summary>
    public static bool IsExcluded(Transform target, Transform stopAt)
    {
        for (var current = target; current != null; current = current.parent)
        {
            if (current.TryGetComponent<ShadowExcludedPart>(out _))
            {
                return true;
            }

            if (current == stopAt)
            {
                break;
            }
        }

        return false;
    }
}
