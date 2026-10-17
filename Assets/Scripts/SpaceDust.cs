using UnityEngine;

/// <summary>
/// Cloud of tiny motionless specks around the player. They stay fixed in the world, so flying
/// through them gives a sense of speed and direction. Particles that leave the volume wrap around to
/// the opposite side, so the density around the player stays constant while using a fixed pool.
/// </summary>
public class SpaceDust : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] Material material;
    [SerializeField] int count = 500;
    [SerializeField] float radius = 30f;
    [SerializeField] Vector2 sizeRange = new Vector2(0.04f, 0.12f);
    [SerializeField] Color colorA = new Color(0.85f, 0.9f, 1f, 1f);
    [SerializeField] Color colorB = new Color(0.55f, 0.6f, 1f, 1f);
    [SerializeField] int seed = 4321;

    ParticleSystem system;
    ParticleSystem.Particle[] particles;
    Color32[] baseColors;

    void Start()
    {
        if (target == null)
        {
            var player = FindFirstObjectByType<PlayerController>();
            target = player != null ? player.transform : (Camera.main != null ? Camera.main.transform : transform);
        }

        BuildSystem();
        Scatter();
    }

    void BuildSystem()
    {
        system = gameObject.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = system.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = count;
        main.startLifetime = 1_000_000f;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;

        var emission = system.emission;
        emission.enabled = false;
        var shape = system.shape;
        shape.enabled = false;

        var psr = GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;
        if (material != null) psr.sharedMaterial = material;

        particles = new ParticleSystem.Particle[count];
        baseColors = new Color32[count];
    }

    void Scatter()
    {
        var rng = new System.Random(seed);
        Vector3 center = target.position;

        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new Vector3(Rand(rng, -radius, radius), Rand(rng, -radius, radius), Rand(rng, -radius, radius));
            baseColors[i] = Color.Lerp(colorA, colorB, (float)rng.NextDouble());

            particles[i].position = center + offset;
            particles[i].velocity = Vector3.zero;
            particles[i].startSize = Rand(rng, sizeRange.x, sizeRange.y);
            particles[i].startLifetime = 1_000_000f;
            particles[i].remainingLifetime = 1_000_000f;
            particles[i].startColor = baseColors[i];
        }

        system.SetParticles(particles, count);
        system.Play();
    }

    void LateUpdate()
    {
        if (system == null || target == null || particles == null) return;

        Vector3 center = target.position;
        float fadeStart = radius * 0.6f;

        for (int i = 0; i < count; i++)
        {
            Vector3 rel = particles[i].position - center;
            rel.x = Mathf.Repeat(rel.x + radius, radius * 2f) - radius;
            rel.y = Mathf.Repeat(rel.y + radius, radius * 2f) - radius;
            rel.z = Mathf.Repeat(rel.z + radius, radius * 2f) - radius;
            particles[i].position = center + rel;

            // fade out near the edge of the volume so wrapping is not visible
            float edge = Mathf.Max(Mathf.Abs(rel.x), Mathf.Max(Mathf.Abs(rel.y), Mathf.Abs(rel.z)));
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, radius, edge));
            Color32 c = baseColors[i];
            c.a = (byte)(alpha * 255f);
            particles[i].startColor = c;
        }

        system.SetParticles(particles, count);
    }

    static float Rand(System.Random rng, float min, float max)
    {
        return Mathf.Lerp(min, max, (float)rng.NextDouble());
    }
}
