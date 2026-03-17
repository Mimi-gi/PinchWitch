using UnityEngine;

public class PlaneStageSetter : MonoBehaviour
{
    [SerializeField] ShadowColliderBuilder[] builders;

    public bool IsShadow;

    void Start()
    {
        foreach (var builder in builders)
        {
            builder.Shadow = IsShadow;
        }
    }
}
