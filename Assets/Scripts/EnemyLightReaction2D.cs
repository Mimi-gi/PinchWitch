using UnityEngine;

// 敵の被光反応。毎フレーム LightRegionRegistry.IsPointLit で判定し、
// 被光中は減速(水平パトロール)/停止(垂直パトロール)しつつHPを減らす。
// HP0で接触死無効化・コライダーTrigger化・スプライト変色を行う。
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemyHealth2D))]
public sealed class EnemyLightReaction2D : MonoBehaviour
{
    [SerializeField] private EnemyHealth2D health;
    [SerializeField] private EnemyPatrolMover2D patrolMover;
    [SerializeField] private EnemyTouchRespawn2D touchRespawn;
    [SerializeField] private Collider2D[] bodyColliders;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform litCheckPoint;

    [SerializeField] private float damagePerSecond = 20f;
    [SerializeField, Range(0f, 1f)] private float litSpeedMultiplierHorizontal = 0.5f;
    [SerializeField] private Color depletedTint = new Color(0.4f, 0.4f, 0.4f, 1f);

    private Color _originalColor;
    private bool _colorCaptured;
    private bool[] _originalIsTrigger;

    private void OnEnable()
    {
        AutoAssignMissingReferences();

        if (health != null)
        {
            health.HealthDepleted += HandleHealthDepleted;
            health.HealthRestored += HandleHealthRestored;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.HealthDepleted -= HandleHealthDepleted;
            health.HealthRestored -= HandleHealthRestored;
        }
    }

    private void Update()
    {
        if (health == null || health.IsDepleted)
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
        patrolMover?.SetExternalSpeedMultiplier(0f);
        touchRespawn?.SetLethal(false);
        SetCollidersTrigger(true);
        ApplyTint(depletedTint);
    }

    private void HandleHealthRestored()
    {
        touchRespawn?.SetLethal(true);
        SetCollidersTrigger(false);
        RestoreTint();
        patrolMover?.SetExternalSpeedMultiplier(1f);
    }

    private void SetCollidersTrigger(bool isTrigger)
    {
        if (bodyColliders == null || bodyColliders.Length == 0)
        {
            return;
        }

        if (isTrigger && _originalIsTrigger == null)
        {
            _originalIsTrigger = new bool[bodyColliders.Length];
            for (var i = 0; i < bodyColliders.Length; i++)
            {
                _originalIsTrigger[i] = bodyColliders[i] != null && bodyColliders[i].isTrigger;
            }
        }

        for (var i = 0; i < bodyColliders.Length; i++)
        {
            if (bodyColliders[i] == null)
            {
                continue;
            }

            bodyColliders[i].isTrigger = isTrigger || (_originalIsTrigger != null && _originalIsTrigger[i]);
        }
    }

    private void ApplyTint(Color color)
    {
        if (spriteRenderer == null)
        {
            return;
        }

        if (!_colorCaptured)
        {
            _originalColor = spriteRenderer.color;
            _colorCaptured = true;
        }

        spriteRenderer.color = color;
    }

    private void RestoreTint()
    {
        if (spriteRenderer == null || !_colorCaptured)
        {
            return;
        }

        spriteRenderer.color = _originalColor;
    }

    private void AutoAssignMissingReferences()
    {
        if (health == null) health = GetComponent<EnemyHealth2D>();
        if (patrolMover == null) patrolMover = GetComponent<EnemyPatrolMover2D>();
        if (touchRespawn == null) touchRespawn = GetComponent<EnemyTouchRespawn2D>();
        if (spriteRenderer == null) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        if (bodyColliders == null || bodyColliders.Length == 0) bodyColliders = GetComponents<Collider2D>();
    }
}
