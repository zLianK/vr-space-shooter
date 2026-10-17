using UnityEngine;

/// <summary>
/// Straight-flying energy bolt. Moves itself (no physics body) and checks for hits along the
/// path every frame so fast bolts never tunnel through objects.
/// </summary>
public class Projectile : MonoBehaviour
{
    Vector3 velocity;
    float timeLeft;
    Transform owner;

    float damage;

    public void Launch(Vector3 direction, float speed, float lifetime, float damageAmount, Transform ownerRoot)
    {
        velocity = direction.normalized * speed;
        timeLeft = lifetime;
        damage = damageAmount;
        owner = ownerRoot;
        transform.rotation = Quaternion.LookRotation(direction);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 step = velocity * dt;

        if (Physics.Raycast(transform.position, step, out RaycastHit hit, step.magnitude, ~0, QueryTriggerInteraction.Ignore)
            && (owner == null || !hit.transform.IsChildOf(owner)))
        {
            OnHit(hit);
            return;
        }

        transform.position += step;

        timeLeft -= dt;
        if (timeLeft <= 0f) Destroy(gameObject);
    }

    // Damages whatever was hit if it has Health. Hook for impact effects too.
    void OnHit(RaycastHit hit)
    {
        var health = hit.collider.GetComponentInParent<Health>();
        if (health != null) health.TakeDamage(damage);

        Destroy(gameObject);
    }
}
