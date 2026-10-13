using UnityEngine;

// 敵の被光反応(暫定仕様。光の仕様確定後にフラッシュ方式へ置き換える予定)。
// 毎フレーム LightRegionRegistry.IsPointLit で判定し、被光中は減速(水平パトロール)/停止(垂直パトロール)しつつHPを減らす。
// HP0 で EnemyStun2D によるスタンに入り、HP は全快に戻す。スタン中も接触死は有効。
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth2D))]
[RequireComponent(typeof(EnemyStun2D))]
public sealed class EnemyLightReaction2D : MonoBehaviour
{
    [SerializeField] private EnemyHealth2D health;
    [SerializeField] private EnemyStun2D stun;
    [SerializeField] private EnemyPatrolMover2D patrolMover;
    [SerializeField] private Transform litCheckPoint;

    [SerializeField] private float damagePerSecond = 20f;
    [SerializeField, Range(0f, 1f)] private float litSpeedMultiplierHorizontal = 0.5f;
    [SerializeField] private float stunDuration = 3f;

    private void OnEnable()
    {
        AutoAssignMissingReferences();

        if (health != null)
        {
            health.HealthDepleted += HandleHealthDepleted;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.HealthDepleted -= HandleHealthDepleted;
        }
    }

    private void Update()
    {
        if (health == null || (stun != null && stun.IsStunned))
        {
            return;
        }

        var checkPoint = litCheckPoint != null ? litCheckPoint.position : transform.position;
        var isLit = LightRegionRegistry.IsPointLit(checkPoint);

        patrolMover?.SetExternalSpeedMultiplier(isLit ? GetLitSpeedMultiplier() : 1f);

        if (isLit)
        {
            health.ApplyDamage(damagePerSecond * Time.deltaTime);
        }
    }

    private float GetLitSpeedMultiplier()
    {
        if (patrolMover == null)
        {
            return 1f;
        }

        return patrolMover.Axis == EnemyPatrolMover2D.PatrolAxis.Horizontal ? litSpeedMultiplierHorizontal : 0f;
    }

    private void HandleHealthDepleted()
    {
        stun?.Stun(stunDuration);

        // スタン明けに再び削れるよう HP を戻す。
        health.ResetRuntimeState();
    }

    private void AutoAssignMissingReferences()
    {
        if (health == null) health = GetComponent<EnemyHealth2D>();
        // 既存 Prefab には EnemyStun2D が付いていないことがあるため、実行時に補う。
        if (stun == null && !TryGetComponent(out stun) && Application.isPlaying) stun = gameObject.AddComponent<EnemyStun2D>();
        if (patrolMover == null) patrolMover = GetComponent<EnemyPatrolMover2D>();
    }
}
