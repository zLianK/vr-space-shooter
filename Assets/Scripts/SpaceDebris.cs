using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// TEMPORARY: scatters static asteroids around the origin so there are visual references to judge
/// movement in empty space. Delete the "Space Debris" object once real level content exists.
///
/// Runs in edit mode too, so the rocks are visible in the Scene view without pressing Play.
/// The rocks are generated, not saved in the scene.
/// </summary>
[ExecuteAlways]
public class SpaceDebris : MonoBehaviour
{
    [Tooltip("Prefab whose direct children are the asteroid models to scatter (e.g. the asteroid belt group). Empty = plain cubes and spheres.")]
    [SerializeField] GameObject asteroidGroup;
    [Tooltip("Only used for the plain cubes and spheres.")]
    [SerializeField] Material material;
    [SerializeField] int count = 60;
    [SerializeField] float minDistance = 10f;
    [SerializeField] float maxDistance = 90f;
    [Tooltip("Rock diameter in meters (the largest side of the model).")]
    [SerializeField] Vector2 sizeRange = new Vector2(2f, 6f);
    [Tooltip("Hits needed to destroy a rock per meter of size (a 3 m rock with 1 = 3 hits).")]
    [SerializeField] float healthPerMeter = 1f;
    [SerializeField] int seed = 1234;

    const string RockName = "Debris";

    /// <summary>Asteroids shot down since the game started (shown on a cockpit gauge).</summary>
    public static int DestroyedCount { get; private set; }

    void OnEnable()
    {
        Clear();
        Spawn();
    }

    void OnDisable()
    {
        Clear();
    }

    void Spawn()
    {
        if (Application.isPlaying) DestroyedCount = 0;
        var rng = new System.Random(seed);
        var models = GetModels();
        for (int i = 0; i < count; i++)
        {
            GameObject rock = models.Count > 0
                ? Instantiate(models[rng.Next(models.Count)])
                : GameObject.CreatePrimitive(rng.Next(2) == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            rock.name = RockName;
            rock.hideFlags = HideFlags.DontSave; // generated at load, never written into the scene
            rock.transform.SetParent(transform, false);

            Vector3 dir = RandomDirection(rng);
            float dist = Mathf.Lerp(minDistance, maxDistance, (float)rng.NextDouble());
            rock.transform.position = transform.position + dir * dist;
            rock.transform.rotation = RandomRotation(rng);
            float size = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble());

            if (models.Count > 0) FitModel(rock, size);
            else
            {
                rock.transform.localScale = Vector3.one * size;
                if (material != null) rock.GetComponent<Renderer>().sharedMaterial = material;
            }

            // bigger rocks take more hits to destroy (only while playing)
            if (Application.isPlaying)
            {
                var health = rock.AddComponent<Health>();
                health.SetMax(Mathf.Ceil(size * healthPerMeter));
                health.Died += _ => DestroyedCount++;
            }
        }
    }

    // each direct child of the group prefab that has a mesh is one asteroid model
    List<GameObject> GetModels()
    {
        var list = new List<GameObject>();
        if (asteroidGroup == null) return list;
        foreach (Transform child in asteroidGroup.transform)
            if (child.GetComponent<MeshFilter>() != null) list.Add(child.gameObject);
        return list;
    }

    // scales the model so its largest side is `size` meters and gives it a sphere collider (needed for shots)
    static void FitModel(GameObject rock, float size)
    {
        var mesh = rock.GetComponent<MeshFilter>().sharedMesh;
        float extent = Mathf.Max(0.001f, Mathf.Max(mesh.bounds.size.x, mesh.bounds.size.y, mesh.bounds.size.z));
        rock.transform.localScale = Vector3.one * (size / extent);

        foreach (var old in rock.GetComponents<Collider>()) DestroyImmediate(old);
        var sphere = rock.AddComponent<SphereCollider>();
        sphere.center = mesh.bounds.center;
        sphere.radius = extent * 0.5f;
    }

    void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (child.name != RockName) continue;

            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    static Vector3 RandomDirection(System.Random rng)
    {
        double z = rng.NextDouble() * 2 - 1;
        double a = rng.NextDouble() * Mathf.PI * 2;
        double r = System.Math.Sqrt(1 - z * z);
        return new Vector3((float)(r * System.Math.Cos(a)), (float)(r * System.Math.Sin(a)), (float)z);
    }

    static Quaternion RandomRotation(System.Random rng)
    {
        return Quaternion.Euler((float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 360f);
    }
}
