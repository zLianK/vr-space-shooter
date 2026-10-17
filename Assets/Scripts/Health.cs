using System;
using UnityEngine;

/// <summary>
/// Hit points for any object that can be damaged. Internal only: there is no health bar.
/// When health reaches zero the object is destroyed (unless <see cref="destroyOnDeath"/> is off,
/// in which case listen to <see cref="Died"/> and handle it yourself).
/// </summary>
public class Health : MonoBehaviour
{
    [SerializeField] float maxHealth = 3f;
    [SerializeField] bool destroyOnDeath = true;

    float current;
    bool dead;

    public float Max => maxHealth;
    public float Current => current;
    public bool IsDead => dead;

    public event Action<Health> Died;

    void Awake()
    {
        current = maxHealth;
    }

    public void SetMax(float value, bool refill = true)
    {
        maxHealth = Mathf.Max(1f, value);
        if (refill) current = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        if (dead || amount <= 0f) return;

        current = Mathf.Max(0f, current - amount);
        if (current > 0f) return;

        dead = true;
        Died?.Invoke(this);
        if (destroyOnDeath) Destroy(gameObject);
    }
}
