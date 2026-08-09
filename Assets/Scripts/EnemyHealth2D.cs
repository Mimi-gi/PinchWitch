using System;
using UnityEngine;

// 敵のHP。光ギミックで被光すると EnemyLightReaction2D から ApplyDamage が呼ばれる。
[DisallowMultipleComponent]
public sealed class EnemyHealth2D : MonoBehaviour, IRuntimeResettable
{
    [SerializeField] private float maxHealth = 100f;

    private float _currentHealth;
    private bool _isDepleted;

    public event Action HealthDepleted;
    public event Action HealthRestored;

    public int ResetOrder => 90;
    public float CurrentHealth => _currentHealth;
    public float MaxHealth => maxHealth;
    public bool IsDepleted => _isDepleted;

    private void Awake()
    {
        _currentHealth = maxHealth;
    }

    public void ApplyDamage(float amount)
    {
        if (_isDepleted || amount <= 0f)
        {
            return;
        }

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        if (_currentHealth <= 0f)
        {
            _isDepleted = true;
            HealthDepleted?.Invoke();
        }
    }

    public void ResetRuntimeState()
    {
        var wasDepleted = _isDepleted;
        _currentHealth = maxHealth;
        _isDepleted = false;

        if (wasDepleted)
        {
            HealthRestored?.Invoke();
        }
    }
}
