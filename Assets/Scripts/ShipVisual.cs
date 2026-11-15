using UnityEngine;

/// <summary>
/// Builds a minimal, futuristic, fully enclosed first-person cockpit out of primitives as a child
/// of this object: floor, ceiling, back wall, side walls with window openings, a front cowl below
/// the windshield, and a tilted dashboard at the bottom of the view. Forward is +Z.
///
/// The "Dashboard" child is the place for future gameplay UI: its "DashboardScreen" child is a flat
/// surface facing the pilot (local +Y), ready for a world-space Canvas, a RenderTexture, etc.
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

    [Header("Ship model")]
    [Tooltip("When set, this ship replaces the primitive cockpit. Must have its interior at the pilot's seat.")]
    [SerializeField] GameObject shipPrefab;
    [Tooltip("Cockpit height in meters the model is scaled to (0 = keep the model's own scale).")]
    [SerializeField] float cockpitHeight = 1.6f;
    [Tooltip("Pilot eye position (local to the player) that the cockpit is centered on.")]
    [SerializeField] Vector3 eyePosition = new Vector3(0f, 0.1f, 0f);
    [Tooltip("Where the seat sits relative to the eye (negative Y = seat below the eyes). Fine-tune the pilot position here.")]
    [SerializeField] Vector3 seatOffset = new Vector3(0f, -0.4f, 0f);

    [Header("Throttle lever")]
    [Tooltip("Degrees the lever tilts forward per throttle level.")]
    [SerializeField] float leverAngleStep = 20f;
    [SerializeField] float leverSpeed = 120f;
    [Tooltip("VR: how close (meters) the right controller's pointing ray must pass to the lever to select it.")]
    [SerializeField] float leverGrabRadius = 0.15f;
    [Tooltip("VR: hand travel in meters along the ship's forward axis per throttle level.")]
    [SerializeField] float leverTravelPerLevel = 0.08f;
    [Tooltip("Tint of the lever while it is pointed at or held.")]
    [SerializeField] Color leverHighlight = new Color(1f, 0.8f, 0.2f);

    [Header("Flight stick")]
    [Tooltip("Degrees the stick tilts at full deflection (forward / back and to the sides).")]
    [SerializeField] float stickMaxAngle = 20f;
    [Tooltip("How fast the stick follows its input, in full deflections per second (also how fast it springs back to center).")]
    [SerializeField] float stickSpeed = 8f;
    [Tooltip("VR: how close (meters) the controller must be to the stick handle to grab it.")]
    [SerializeField] float stickGrabRadius = 0.15f;
    [Tooltip("VR: hand travel in meters (forward / back or sideways) for full deflection.")]
    [SerializeField] float stickTravel = 0.07f;

    [Header("Weapon switches")]
    [Tooltip("Names of the three cockpit switches (left of the panel) that select weapon 1, 2 and 3.")]
    [SerializeField] string[] weaponSwitchNames =
        { "CockpitEquipments_Switch2-1", "CockpitEquipments_Switch2-2", "CockpitEquipments_Switch2-3" };
    [Tooltip("Degrees the selected switch tips sideways (around the ship's forward axis). Unselected ones stay up. Negative flips the side.")]
    [SerializeField] float switchTiltAngle = 40f;
    [SerializeField] float switchTiltSpeed = 240f;
    [Tooltip("Strength of the glow (emission and a small light) on the selected switch.")]
    [SerializeField] float switchGlow = 3f;
    [Tooltip("VR: how close (meters) the pointing ray must pass to a switch to select it.")]
    [SerializeField] float switchPickRadius = 0.04f;

    [Header("Gauges")]
    [Tooltip("Cockpit gauge face that shows how many asteroids were destroyed.")]
    [SerializeField] string counterGaugeName = "CockpitEquipments_Gauge1-Screen";
    [SerializeField] Color counterColor = new Color(0.3f, 1f, 0.9f);
    [Tooltip("Cockpit gauge face that shows the countdown.")]
    [SerializeField] string timerGaugeName = "CockpitEquipments_Gauge2-Screen";
    [SerializeField] Color timerColor = new Color(1f, 0.8f, 0.2f);
    [SerializeField] Color timerLowColor = new Color(1f, 0.25f, 0.2f);
    [Tooltip("The timer turns to the low color at or below this many seconds.")]
    [SerializeField] float timerLowSeconds = 10f;
    [Tooltip("Blank the printed dial of that gauge (its numbers are part of the texture) so only the counter shows.")]
    [SerializeField] bool blankGaugeFace = true;
    [Tooltip("Digit height as a share of the gauge face.")]
    [SerializeField] float counterSize = 0.4f;

    [Header("Primitive cockpit (used when no ship model is set)")]
    [Tooltip("Dark structure (frame, panels).")]
    [SerializeField] Material hull;
    [Tooltip("Neon trim lines.")]
    [SerializeField] Material accent;
    [Tooltip("Glowing screens.")]
    [SerializeField] Material glass;
    [Tooltip("Warm indicator lights.")]
    [SerializeField] Material engine;

    PlayerController player;
    Transform[] leverHandles;
    Vector3[] leverPositions;
    Quaternion[] leverRotations;
    Transform leverPivot;
    float leverAngle;
    ShipWeapons weapons;
    int[] switchState;
    int hoverMask; // bit i set = a controller points at weapon switch i
    Renderer[] leverRenderers;
    MaterialPropertyBlock leverBlock;
    bool leverTinted;
    Transform stickHandle;
    Renderer stickBase;
    Renderer[] stickRenderers;
    Vector3 stickPosition;
    Quaternion stickRotation;
    Vector2 stickShown;
    MaterialPropertyBlock stickBlock;
    bool stickTinted;

    // One per controller: both hands can point at and press every cockpit control.
    // To add a control: pick it in UpdatePointer (ray test), react to `pressed` (rising edge) / `down` (held).
    class Pointer
    {
        public UnityEngine.XR.XRNode node;
        public bool wasDown, holdingLever, hoverLever, holdingStick, nearStick;
        public int hoverSwitch = -1;
        public float grabHandAxis;
        public int grabLevel;
        public Vector3 grabStickHand;
    }

    readonly Pointer[] pointers =
    {
        new Pointer { node = UnityEngine.XR.XRNode.LeftHand },
        new Pointer { node = UnityEngine.XR.XRNode.RightHand },
    };

    void Start()
    {
        player = GetComponent<PlayerController>();
        weapons = GetComponent<ShipWeapons>();
        if (shipPrefab != null) BuildFromPrefab();
        else Build();
    }

    // Instantiates the ship model and moves it so the pilot's eye sits inside its cockpit.
    void BuildFromPrefab()
    {
        var ship = Instantiate(shipPrefab, transform);
        ship.name = "Ship";
        ship.transform.localPosition = Vector3.zero;
        ship.transform.localRotation = Quaternion.identity;

        // visual only: the player's own collider handles physics
        foreach (var col in ship.GetComponentsInChildren<Collider>()) Destroy(col);

        var renderers = ship.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        // the cockpit parts are named after the module ("Cockpit3_WithInterior", "CockpitEquipments_...")
        bool found = false;
        Bounds cockpit = default, all = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (i == 0) all = renderers[0].bounds; else all.Encapsulate(renderers[i].bounds);

            if (!IsCockpitPart(renderers[i].transform, ship.transform)) continue;
            if (!found) { cockpit = renderers[i].bounds; found = true; }
            else cockpit.Encapsulate(renderers[i].bounds);
        }
        if (!found) cockpit = all;

        if (cockpitHeight > 0f && cockpit.size.y > 0.01f)
            ship.transform.localScale = Vector3.one * (cockpitHeight / cockpit.size.y);

        // re-measure after scaling; the pilot's head goes just above the seat ("CockpitEquipments_Seat"),
        // falling back to the middle of the cockpit
        Bounds anchor = default;
        bool hasAnchor = false;
        foreach (var r in renderers)
        {
            if (!HasNamedAncestor(r.transform, ship.transform, "Seat") || HasNamedAncestor(r.transform, ship.transform, "SeatEjector")) continue;
            if (!hasAnchor) { anchor = r.bounds; hasAnchor = true; } else anchor.Encapsulate(r.bounds);
        }
        if (!hasAnchor)
        {
            foreach (var r in renderers)
            {
                if (found && !IsCockpitPart(r.transform, ship.transform)) continue;
                if (!hasAnchor) { anchor = r.bounds; hasAnchor = true; } else anchor.Encapsulate(r.bounds);
            }
        }
        FindLever(ship.transform);
        FindStick(ship.transform);
        FindSwitches(ship.transform);
        Vector3 center = transform.InverseTransformPoint(anchor.center);
        ship.transform.localPosition = eyePosition + seatOffset - center;
        SetupCounter(ship.transform);
    }

    // Text written on the face of a cockpit gauge, readable from the pilot's seat.
    class GaugeText
    {
        public TextMesh text;
        public Renderer renderer;
        public float face;
        public bool needsFit;
        public string shown;
    }

    GaugeText counter, timer;

    GaugeText SetupGaugeText(Transform root, string screenName, Color color)
    {
        Transform screen = null;
        foreach (var t in root.GetComponentsInChildren<Transform>())
            if (t.name == screenName) { screen = t; break; }
        var filter = screen != null ? screen.GetComponent<MeshFilter>() : null;
        var gaugeRenderer = screen != null ? screen.GetComponent<Renderer>() : null;
        if (filter == null || gaugeRenderer == null) return null;

        // dial center, seen from the pilot: the digits sit on the line of sight to it, just in front of the gauge,
        // so they look centered whichever way the dial's mesh is oriented
        Vector3 center = gaugeRenderer.bounds.center;
        Vector3 toEye = transform.TransformPoint(eyePosition) - center;
        Vector3 view = toEye.normalized;

        var gauge = screen.parent != null ? screen.parent : screen;
        Bounds all = gaugeRenderer.bounds;
        foreach (var r in gauge.GetComponentsInChildren<Renderer>()) all.Encapsulate(r.bounds);
        float front = 0f;
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = all.center + Vector3.Scale(all.extents, new Vector3((i & 1) * 2 - 1, ((i >> 1) & 1) * 2 - 1, ((i >> 2) & 1) * 2 - 1));
            front = Mathf.Max(front, Vector3.Dot(corner - center, view));
        }

        Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, screen.lossyScale);
        var result = new GaugeText { face = Mathf.Max(size.x, size.y, size.z), needsFit = true };

        if (blankGaugeFace) BlankFace(gaugeRenderer);

        var go = new GameObject(screenName + "_Text");
        go.transform.SetParent(root, true);
        go.transform.position = center + view * (front + 0.004f);
        go.transform.rotation = Quaternion.LookRotation(-view, transform.up);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        result.text = go.AddComponent<TextMesh>();
        result.text.font = font;
        result.text.fontSize = 64;
        result.text.characterSize = 0.05f;
        result.text.anchor = TextAnchor.MiddleCenter;
        result.text.alignment = TextAlignment.Center;
        result.text.color = color;
        result.text.text = "0";
        result.renderer = go.GetComponent<MeshRenderer>();
        result.renderer.sharedMaterial = font.material;
        result.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        // drawn after the cockpit, so nothing paints over the digits, but before the menu panels (GameFlow, 3999+),
        // so the digits never show through a menu
        result.renderer.material.renderQueue = 3990;
        return result;
    }

    void SetupCounter(Transform root)
    {
        counter = SetupGaugeText(root, counterGaugeName, counterColor);
        timer = SetupGaugeText(root, timerGaugeName, timerColor);
    }

    // The dial numbers are painted into the texture, so give this gauge its own plain dark material.
    static void BlankFace(Renderer face)
    {
        var materials = face.materials; // own copies: other gauges sharing the texture keep their dial
        foreach (var m in materials)
        {
            foreach (var texture in new[] { "_BaseMap", "_MainTex", "_EmissionMap" })
                if (m.HasProperty(texture)) m.SetTexture(texture, null);
            foreach (var color in new[] { "_BaseColor", "_Color" })
                if (m.HasProperty(color)) m.SetColor(color, new Color(0.02f, 0.03f, 0.04f));
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
        }
        face.materials = materials;
    }

    void SetGaugeText(GaugeText gauge, string value)
    {
        if (gauge == null) return;

        // the text mesh only has a size after it has been built, so scale it to the gauge once that exists
        float height = gauge.renderer.localBounds.size.y * gauge.text.transform.lossyScale.y;
        if (gauge.needsFit && height > 0.0001f)
        {
            gauge.text.transform.localScale *= gauge.face * counterSize / height;
            gauge.needsFit = false;
        }

        if (value == gauge.shown) return;
        gauge.shown = value;
        gauge.text.text = value;
    }

    void UpdateCounter()
    {
        SetGaugeText(counter, SpaceDebris.DestroyedCount.ToString());

        if (timer == null) return;
        float left = GameFlow.TimeLeft; // the match length and clock live in GameFlow
        timer.text.color = left <= timerLowSeconds ? timerLowColor : timerColor;
        SetGaugeText(timer, Mathf.CeilToInt(left).ToString());
    }

    // The right-hand lever: two handle meshes that tilt around the throttle base.
    class WeaponSwitch
    {
        public Transform transform;
        public Vector3 position;
        public Quaternion rotation;
        public Material[] materials;
        public Color[] baseColors;
        public Light glow;
        public float angle;
        public Renderer[] renderers;
    }

    WeaponSwitch[] switches;

    static string ColorProperty(Material m) => m.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";

    void FindSwitches(Transform root)
    {
        switches = new WeaponSwitch[weaponSwitchNames.Length];
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            int i = System.Array.IndexOf(weaponSwitchNames, t.name);
            if (i < 0) continue;

            var sw = new WeaponSwitch { transform = t, position = t.localPosition, rotation = t.localRotation };
            sw.renderers = t.GetComponentsInChildren<Renderer>();
            sw.materials = new Material[sw.renderers.Length];
            sw.baseColors = new Color[sw.renderers.Length];
            for (int r = 0; r < sw.renderers.Length; r++)
            {
                sw.materials[r] = sw.renderers[r].material; // own copy, so only this switch changes
                sw.baseColors[r] = sw.materials[r].HasProperty(ColorProperty(sw.materials[r]))
                    ? sw.materials[r].GetColor(ColorProperty(sw.materials[r])) : Color.white;
            }

            var lightObject = new GameObject("SwitchGlow");
            lightObject.transform.SetParent(t, false);
            lightObject.transform.localPosition = Vector3.zero;
            sw.glow = lightObject.AddComponent<Light>();
            sw.glow.type = LightType.Point;
            sw.glow.range = 0.5f;
            sw.glow.enabled = false;

            switches[i] = sw;
        }
        switchState = new int[switches.Length];
        for (int i = 0; i < switchState.Length; i++) switchState[i] = -1;
    }

    // The selected weapon's switch tips sideways and glows in the weapon's color; the others stay up.
    // The switch being pointed at gets the highlight color.
    void UpdateSwitches()
    {
        if (switches == null || weapons == null) return;

        for (int i = 0; i < switches.Length; i++)
        {
            var sw = switches[i];
            if (sw == null) continue;

            bool selected = i == weapons.CurrentWeapon;

            sw.angle = Mathf.MoveTowards(sw.angle, selected ? switchTiltAngle : 0f, switchTiltSpeed * Time.deltaTime);
            sw.transform.localPosition = sw.position;
            sw.transform.localRotation = sw.rotation;
            sw.transform.RotateAround(sw.transform.position, transform.forward, sw.angle);

            int state = selected ? 2 : (hoverMask & (1 << i)) != 0 ? 1 : 0;
            if (state == switchState[i]) continue;
            switchState[i] = state;

            Color color = state == 2 ? weapons.WeaponColor(i) : leverHighlight;
            for (int m = 0; m < sw.materials.Length; m++)
            {
                var mat = sw.materials[m];
                mat.SetColor(ColorProperty(mat), state == 0 ? sw.baseColors[m] : color);
                if (state == 2)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * switchGlow);
                }
                else if (mat.HasProperty("_EmissionColor"))
                {
                    mat.SetColor("_EmissionColor", Color.black);
                }
            }
            sw.glow.color = color;
            sw.glow.intensity = switchGlow * 0.3f;
            sw.glow.enabled = state == 2;
        }
    }

    // index of the weapon switch the ray passes closest to (within switchPickRadius), or -1
    int PickSwitch(Vector3 hand, Vector3 dir)
    {
        int best = -1;
        float bestDistance = switchPickRadius;
        for (int i = 0; i < switches.Length; i++)
        {
            if (switches[i] == null || switches[i].renderers.Length == 0) continue;

            Bounds b = switches[i].renderers[0].bounds;
            foreach (var r in switches[i].renderers) b.Encapsulate(r.bounds);

            Vector3 to = b.center - hand;
            float along = Vector3.Dot(to, dir);
            if (along <= 0f) continue;

            float distance = (to - dir * along).magnitude;
            if (distance <= bestDistance) { best = i; bestDistance = distance; }
        }
        return best;
    }

    void FindLever(Transform root)
    {
        var handles = new System.Collections.Generic.List<Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            if (t.name.Contains("ThrottleControl1-Handle")) handles.Add(t);
            else if (t.name.Contains("ThrottleContro1-Base")) leverPivot = t;
        }
        if (leverPivot == null || handles.Count == 0) return;

        leverHandles = handles.ToArray();
        var found = new System.Collections.Generic.List<Renderer>();
        foreach (var h in leverHandles) found.AddRange(h.GetComponentsInChildren<Renderer>());
        leverRenderers = found.ToArray();
        if (leverRenderers.Length == 0) { leverHandles = null; return; }
        leverBlock = new MaterialPropertyBlock();
        leverPositions = new Vector3[leverHandles.Length];
        leverRotations = new Quaternion[leverHandles.Length];
        for (int i = 0; i < leverHandles.Length; i++)
        {
            leverPositions[i] = leverHandles[i].localPosition;
            leverRotations[i] = leverHandles[i].localRotation;
        }
    }

    // The joystick between the pilot's knees: a handle that tilts on its base.
    void FindStick(Transform root)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            if (t.name.Contains("Joystick2-Handle")) stickHandle = t;
            else if (t.name.Contains("Joystick2-Base")) stickBase = t.GetComponent<Renderer>();
        }
        if (stickHandle == null || stickBase == null) { stickHandle = null; return; }

        stickRenderers = stickHandle.GetComponentsInChildren<Renderer>();
        if (stickRenderers.Length == 0) { stickHandle = null; return; }
        stickBlock = new MaterialPropertyBlock();
        stickPosition = stickHandle.localPosition;
        stickRotation = stickHandle.localRotation;
        if (player != null) player.HasCockpitStick = true;
    }

    // The stick shows the deflection that steers the ship, whether it comes from the hand (VR) or the keys.
    void UpdateStick()
    {
        if (stickHandle == null) return;

        stickShown = Vector2.MoveTowards(stickShown, player.Stick, stickSpeed * Time.deltaTime);

        // pushed forward = top toward +Z (around the ship's lateral axis), tilted right = top toward +X
        Vector3 pivot = stickBase.bounds.center;
        stickHandle.localPosition = stickPosition;
        stickHandle.localRotation = stickRotation;
        stickHandle.RotateAround(pivot, transform.right, stickShown.y * stickMaxAngle);
        stickHandle.RotateAround(pivot, transform.forward, -stickShown.x * stickMaxAngle);
    }

    void Update()
    {
        if (player == null) return;

        UpdateCounter();
        if (!GameFlow.IsPlaying)
        {
            // menu / pause / result: the cockpit buttons are off, but remember the held buttons so
            // a button still down when the game resumes does not count as a new press
            hoverMask = 0;
            foreach (var pointer in pointers)
            {
                pointer.holdingLever = pointer.hoverLever = pointer.holdingStick = pointer.nearStick = false;
                pointer.hoverSwitch = -1;
                var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(pointer.node);
                pointer.wasDown = device.isValid &&
                    device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool held) && held;
            }
            if (leverHandles != null) SetLeverTint(false);
            if (stickHandle != null) SetStickTint(false);
        }
        else if (UnityEngine.XR.XRSettings.isDeviceActive)
        {
            hoverMask = 0;
            bool leverActive = false, stickActive = false;
            foreach (var pointer in pointers)
            {
                UpdatePointer(pointer);
                if (pointer.hoverSwitch >= 0) hoverMask |= 1 << pointer.hoverSwitch;
                leverActive |= pointer.hoverLever || pointer.holdingLever;
                stickActive |= pointer.nearStick || pointer.holdingStick;
            }
            if (leverHandles != null) SetLeverTint(leverActive);
            if (stickHandle != null) SetStickTint(stickActive);
        }
        UpdateSwitches();
        UpdateStick();
        if (leverHandles == null) return;

        float target = (player.ThrottleLevel - 1) * leverAngleStep;
        leverAngle = Mathf.MoveTowards(leverAngle, target, leverSpeed * Time.deltaTime);

        // tilt around the ship's lateral axis, through the lever base (forward = top of the lever moves toward +Z)
        Vector3 pivot = leverPivot.position;
        Vector3 axis = transform.right;
        for (int i = 0; i < leverHandles.Length; i++)
        {
            var h = leverHandles[i];
            h.localPosition = leverPositions[i];
            h.localRotation = leverRotations[i];
            h.RotateAround(pivot, axis, leverAngle);
        }
    }

    // VR: point the right controller at the lever, hold A and move the hand forward / back to change level.
    // VR: point a controller at a control and press its first face button (A on the right, X on the left).
    void UpdatePointer(Pointer pointer)
    {
        var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(pointer.node);
        if (!device.isValid ||
            !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out Vector3 handLocal) ||
            !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out Quaternion rotLocal))
        {
            pointer.holdingLever = pointer.hoverLever = pointer.holdingStick = pointer.nearStick = pointer.wasDown = false;
            pointer.hoverSwitch = -1;
            return;
        }

        // controller poses are relative to the tracking space (the camera's parent, recentered on the pilot's seat)
        Transform space = player != null && player.TrackingSpace != null ? player.TrackingSpace : transform;
        Vector3 hand = space.TransformPoint(handLocal);
        Vector3 dir = (space.rotation * rotLocal) * Vector3.forward;
        device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool down);

        bool pressed = down && !pointer.wasDown;
        pointer.wasDown = down;

        // flight stick (one hand at a time): grabbed with the hand itself, not the pointing ray.
        // While held, the other cockpit controls ignore this hand.
        if (stickHandle != null)
        {
            bool otherHoldsStick = false;
            foreach (var other in pointers) otherHoldsStick |= other != pointer && other.holdingStick;

            pointer.nearStick = !otherHoldsStick && !pointer.holdingLever &&
                                Vector3.Distance(hand, StickCenter()) <= stickGrabRadius;

            if (!down) pointer.holdingStick = false;
            else if (pressed && pointer.nearStick)
            {
                pointer.holdingStick = true;
                // measured in ship space: in world space the hand moves with the flying ship and the stick would drift
                pointer.grabStickHand = transform.InverseTransformPoint(hand);
                device.SendHapticImpulse(0, 0.3f, 0.05f);
            }

            if (pointer.holdingStick)
            {
                // from where it was grabbed: hand forward = stick pushed forward (nose down), back = nose up,
                // sideways = tilted to that side (turn)
                Vector3 moved = transform.InverseTransformPoint(hand) - pointer.grabStickHand;
                player.HoldStick(new Vector2(moved.x, moved.z) / stickTravel);
                pointer.hoverSwitch = -1;
                pointer.hoverLever = false;
                return;
            }
        }

        // weapon switches
        pointer.hoverSwitch = switches != null ? PickSwitch(hand, dir) : -1;
        if (pressed && pointer.hoverSwitch >= 0 && weapons != null)
        {
            weapons.SelectWeapon(pointer.hoverSwitch);
            device.SendHapticImpulse(0, 0.4f, 0.05f);
        }

        // throttle lever (one hand at a time)
        if (leverHandles == null) return;

        bool otherHolds = false;
        foreach (var other in pointers) otherHolds |= other != pointer && other.holdingLever;

        Vector3 toLever = LeverCenter() - hand;
        float along = Vector3.Dot(toLever, dir);
        pointer.hoverLever = !otherHolds && pointer.hoverSwitch < 0 && along > 0f &&
                             (toLever - dir * along).magnitude <= leverGrabRadius;

        if (!down) pointer.holdingLever = false;
        else if (pressed && pointer.hoverLever)
        {
            pointer.holdingLever = true;
            // measured in ship space: in world space the hand moves with the flying ship and the lever would drift
            pointer.grabHandAxis = transform.InverseTransformPoint(hand).z;
            pointer.grabLevel = player.ThrottleLevel;
            device.SendHapticImpulse(0, 0.3f, 0.05f);
        }

        if (pointer.holdingLever)
        {
            int level = pointer.grabLevel +
                        Mathf.RoundToInt((transform.InverseTransformPoint(hand).z - pointer.grabHandAxis) / leverTravelPerLevel);
            level = Mathf.Clamp(level, 1, player.ThrottleLevels);
            if (level != player.ThrottleLevel)
            {
                player.SetThrottle(level);
                device.SendHapticImpulse(0, 0.5f, 0.06f);
            }
        }
    }

    Vector3 LeverCenter()
    {
        Bounds b = leverRenderers[0].bounds;
        foreach (var r in leverRenderers) b.Encapsulate(r.bounds);
        return b.center;
    }

    Vector3 StickCenter()
    {
        Bounds b = stickRenderers[0].bounds;
        foreach (var r in stickRenderers) b.Encapsulate(r.bounds);
        return b.center;
    }

    void SetStickTint(bool on)
    {
        if (on == stickTinted) return;
        stickTinted = on;
        if (on) stickBlock.SetColor("_BaseColor", leverHighlight); else stickBlock.Clear();
        foreach (var r in stickRenderers) r.SetPropertyBlock(on ? stickBlock : null);
    }

    void SetLeverTint(bool on)
    {
        if (on == leverTinted) return;
        leverTinted = on;
        if (on) leverBlock.SetColor("_BaseColor", leverHighlight); else leverBlock.Clear();
        foreach (var r in leverRenderers) r.SetPropertyBlock(on ? leverBlock : null);
    }

    static bool HasNamedAncestor(Transform t, Transform root, string part)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name.Contains(part)) return true;
        return false;
    }

    static bool IsCockpitPart(Transform t, Transform root)
    {
        for (; t != null && t != root; t = t.parent)
            if (t.name.Contains("Cockpit")) return true;
        return false;
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
