using UnityEngine;

/// <summary>
/// TEMPORARY: scatters static rocks around the origin so there are visual references to judge
/// movement in empty space. Delete the "Space Debris" object once real level content exists.
///
/// Runs in edit mode too, so the rocks are visible in the Scene view without pressing Play.
/// The rocks are generated, not saved in the scene.
/// </summary>
[ExecuteAlways]
public class SpaceDebris : MonoBehaviour
{
    [SerializeField] Material material;
    [SerializeField] int count = 60;
    [SerializeField] float minDistance = 10f;
    [SerializeField] float maxDistance = 90f;
    [SerializeField] Vector2 sizeRange = new Vector2(1f, 5f);
    [Tooltip("Hits needed to destroy a rock per meter of size (a 3 m rock with 1 = 3 hits).")]
    [SerializeField] float healthPerMeter = 1f;
    [SerializeField] int seed = 1234;

    const string RockName = "Debris";

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
        var rng = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            var rock = GameObject.CreatePrimitive(rng.Next(2) == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere);
            rock.name = RockName;
            rock.hideFlags = HideFlags.DontSave; // generated at load, never written into the scene
            rock.transform.SetParent(transform, false);

            Vector3 dir = RandomDirection(rng);
            float dist = Mathf.Lerp(minDistance, maxDistance, (float)rng.NextDouble());
            rock.transform.position = transform.position + dir * dist;
            rock.transform.rotation = RandomRotation(rng);
            float size = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble());
            rock.transform.localScale = Vector3.one * size;

            if (material != null) rock.GetComponent<Renderer>().sharedMaterial = material;

            // bigger rocks take more hits to destroy (only while playing)
            if (Application.isPlaying)
                rock.AddComponent<Health>().SetMax(Mathf.Ceil(size * healthPerMeter));
        }
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
