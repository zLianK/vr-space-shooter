using UnityEngine;

/// <summary>
/// Builds a minimal, futuristic, fully enclosed first-person cockpit out of primitives as a child
/// of this object: floor, ceiling, back wall, side walls with window openings, a front cowl below
/// the windshield, and a tilted dashboard at the bottom of the view. Forward is +Z.
///
/// The "Dashboard" child is the place for future gameplay UI: its "DashboardScreen" child is a flat
/// surface facing the pilot (local +Y), ready for a world-space Canvas, a RenderTexture, etc.
///
/// Placeholder art until a real cockpit model exists: disable this component and add a proper mesh
/// under the player.
/// </summary>
public class ShipVisual : MonoBehaviour
{
    // interior volume (local space, eye is at about (0, 0.1, 0))
    const float HalfWidth = 1.0f;
    const float Floor = -0.6f;
    const float Ceiling = 0.75f;
    const float Back = -1.0f;
    const float Front = 1.1f;
    const float Wall = 0.05f;

    [Tooltip("Dark structure (frame, panels).")]
    [SerializeField] Material hull;
    [Tooltip("Neon trim lines.")]
    [SerializeField] Material accent;
    [Tooltip("Glowing screens.")]
    [SerializeField] Material glass;
    [Tooltip("Warm indicator lights.")]
    [SerializeField] Material engine;

    void Start()
    {
        Build();
    }

    void Build()
    {
        var cockpit = new GameObject("Cockpit").transform;
        cockpit.SetParent(transform, false);

        BuildDashboard(cockpit);
        BuildShell(cockpit);
    }

    // Tilted console. Only its far edge is in the lower part of the view; look down to see all of it.
    void BuildDashboard(Transform parent)
    {
        var dash = new GameObject("Dashboard").transform;
        dash.SetParent(parent, false);
        dash.localPosition = new Vector3(0f, -0.23f, 0.5f);
        dash.localRotation = Quaternion.Euler(-25f, 0f, 0f);

        // base slab
        Part(dash, PrimitiveType.Cube, hull, new Vector3(0f, 0f, 0f), new Vector3(1.5f, 0.12f, 0.5f));

        // neon trim along the far edge and the sides
        Part(dash, PrimitiveType.Cube, accent, new Vector3(0f, 0.062f, 0.245f), new Vector3(1.5f, 0.012f, 0.012f));
        Part(dash, PrimitiveType.Cube, accent, new Vector3(-0.744f, 0.062f, 0f), new Vector3(0.012f, 0.012f, 0.5f));
        Part(dash, PrimitiveType.Cube, accent, new Vector3(0.744f, 0.062f, 0f), new Vector3(0.012f, 0.012f, 0.5f));

        // main screen: reserved for future gameplay functionality
        var screen = Part(dash, PrimitiveType.Cube, glass, new Vector3(0f, 0.064f, 0f), new Vector3(0.9f, 0.01f, 0.34f));
        screen.name = "DashboardScreen";

        // side readouts
        Part(dash, PrimitiveType.Cube, glass, new Vector3(-0.55f, 0.064f, 0f), new Vector3(0.28f, 0.01f, 0.24f));
        Part(dash, PrimitiveType.Cube, engine, new Vector3(0.55f, 0.064f, 0f), new Vector3(0.28f, 0.01f, 0.24f));
    }

    // Closed hull around the pilot. Openings: the windshield (front) and two side windows.
    void BuildShell(Transform parent)
    {
        float width = HalfWidth * 2f;
        float length = Front - Back;
        float midZ = (Front + Back) * 0.5f;
        float height = Ceiling - Floor;

        // floor, ceiling, back wall
        Part(parent, PrimitiveType.Cube, hull, new Vector3(0f, Floor, midZ), new Vector3(width, Wall, length));
        Part(parent, PrimitiveType.Cube, hull, new Vector3(0f, Ceiling, midZ), new Vector3(width, Wall, length));
        Part(parent, PrimitiveType.Cube, hull, new Vector3(0f, (Floor + Ceiling) * 0.5f, Back), new Vector3(width, height, Wall));

        // front cowl below the windshield (hidden behind the dashboard for the most part)
        const float cowlTop = -0.18f;
        Part(parent, PrimitiveType.Cube, hull,
             new Vector3(0f, (Floor + cowlTop) * 0.5f, Front),
             new Vector3(width, cowlTop - Floor, Wall));

        // windshield corner posts
        float postHeight = Ceiling - cowlTop;
        float postY = (Ceiling + cowlTop) * 0.5f;
        Part(parent, PrimitiveType.Cube, hull, new Vector3(-HalfWidth + 0.04f, postY, Front), new Vector3(0.08f, postHeight, 0.08f));
        Part(parent, PrimitiveType.Cube, hull, new Vector3(HalfWidth - 0.04f, postY, Front), new Vector3(0.08f, postHeight, 0.08f));

        // side walls: solid lower panel + roof strip + posts, leaving window openings in between
        const float sillTop = -0.05f;
        const float roofBottom = 0.45f;
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * HalfWidth;

            Part(parent, PrimitiveType.Cube, hull,
                 new Vector3(x, (Floor + sillTop) * 0.5f, midZ),
                 new Vector3(Wall, sillTop - Floor, length));
            Part(parent, PrimitiveType.Cube, hull,
                 new Vector3(x, (roofBottom + Ceiling) * 0.5f, midZ),
                 new Vector3(Wall, Ceiling - roofBottom, length));

            float postY2 = (sillTop + roofBottom) * 0.5f;
            float postH = roofBottom - sillTop;
            Part(parent, PrimitiveType.Cube, hull, new Vector3(x, postY2, -0.45f), new Vector3(Wall, postH, 0.1f));
            Part(parent, PrimitiveType.Cube, hull, new Vector3(x, postY2, 0.35f), new Vector3(Wall, postH, 0.1f));

            // neon rail along the window sill
            Part(parent, PrimitiveType.Cube, accent,
                 new Vector3(x - side * 0.03f, sillTop + 0.02f, midZ),
                 new Vector3(0.012f, 0.015f, length));
        }

        // neon rail along the top of the windshield
        Part(parent, PrimitiveType.Cube, accent,
             new Vector3(0f, Ceiling - 0.04f, Front - 0.03f),
             new Vector3(width - 0.2f, 0.015f, 0.012f));
    }

    static Transform Part(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = type.ToString();

        // visual only: the player's own collider handles physics
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = pos;
        t.localRotation = Quaternion.Euler(euler);
        t.localScale = scale;

        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (mat != null) r.sharedMaterial = mat;
        return t;
    }
}
