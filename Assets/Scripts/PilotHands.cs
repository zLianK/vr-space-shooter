using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Armored pilot gloves that follow the VR controllers: gunmetal plates, dark joints and cyan light strips,
/// built from primitives like the rest of the generated cockpit. Only shown with a headset.
///
/// The fingers follow the controller:
///   index        = trigger (half closed while the finger only rests on the trigger)
///   middle..pinky = grip
///   thumb        = down while touching a face button or the stick, raised otherwise
///
/// The gloves are placed in the controller's grip pose (OpenXR): +Z runs through the fist from the little
/// finger to the thumb, +X points to the back of the right hand (mirrored for the left), -Y toward the knuckles.
/// If they don't sit right on the real controllers, fine-tune the offsets below (given for the right hand).
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PilotHands : MonoBehaviour
{
    [SerializeField] Material armorMaterial;
    [SerializeField] Material jointMaterial;
    [SerializeField] Material glowMaterial;

    [Header("Fit (right hand, mirrored for the left)")]
    [SerializeField] Vector3 positionOffset = Vector3.zero;
    [SerializeField] Vector3 rotationOffset = Vector3.zero;

    [Header("Fingers")]
    [Tooltip("How fast the fingers follow the buttons (higher = snappier).")]
    [SerializeField] float curlSpeed = 14f;

    [Tooltip("Show the gloves in front of the camera without a headset, to check their look in the editor.")]
    [SerializeField] bool previewWithoutHeadset;

    // drawn after the menu overlay, so a hand reaching toward the menu stays in front of it
    const int HandQueue = 4003;

    // capacitive touch, as named by the OpenXR / Oculus providers (missing names just read as "not touched")
    static readonly InputFeatureUsage<bool> TriggerTouch = new InputFeatureUsage<bool>("TriggerTouch");
    static readonly InputFeatureUsage<bool> IndexTouch = new InputFeatureUsage<bool>("IndexTouch");
    static readonly InputFeatureUsage<bool> ThumbTouch = new InputFeatureUsage<bool>("ThumbTouch");
    static readonly InputFeatureUsage<bool> ThumbrestTouch = new InputFeatureUsage<bool>("ThumbrestTouch");

    class Finger
    {
        public Transform[] joints;
        public Quaternion baseRotation;
        public Vector3 axis;          // curl axis, in the space of the first joint
        public float[] open, closed;  // joint angles in degrees
        public float curl, target;
    }

    class Hand
    {
        public XRNode node;
        public Transform root;
        public Finger thumb, index;
        public Finger[] grip;         // middle, ring, pinky
    }

    PlayerController player;
    Material armor, joint, glow;
    Hand[] hands;
    bool preview;

    void Awake()
    {
        player = GetComponent<PlayerController>();
    }

    void OnEnable() => Application.onBeforeRender += UpdatePoses;
    void OnDisable() => Application.onBeforeRender -= UpdatePoses;

    void OnDestroy()
    {
        if (hands != null)
            foreach (var hand in hands) if (hand.root != null) Destroy(hand.root.gameObject);
        Destroy(armor); Destroy(joint); Destroy(glow);
    }

    void Update()
    {
        if (hands == null)
        {
            if (XRSettings.isDeviceActive) Build(player.TrackingSpace, false);
            else if (previewWithoutHeadset && Camera.main != null) Build(Camera.main.transform, true);
            else return;
        }

        float dt = Time.unscaledDeltaTime; // fingers keep moving in menus (time is stopped there)
        foreach (var hand in hands)
        {
            ReadFingers(hand);
            foreach (var finger in AllFingers(hand))
            {
                finger.curl = Mathf.MoveTowards(finger.curl, finger.target, curlSpeed * dt);
                Pose(finger);
            }
        }
        UpdatePoses();
    }

    static Finger[] AllFingers(Hand hand) => new[] { hand.thumb, hand.index, hand.grip[0], hand.grip[1], hand.grip[2] };

    // ---------------------------------------------------------------- input

    void ReadFingers(Hand hand)
    {
        if (preview)
        {
            // a fist on the right, a relaxed open hand pointing with the index on the left
            bool right = hand.node == XRNode.RightHand;
            hand.index.target = right ? 0.9f : 0f;
            foreach (var f in hand.grip) f.target = right ? 1f : 0.85f;
            hand.thumb.target = right ? 1f : 0.2f;
            return;
        }

        var device = InputDevices.GetDeviceAtXRNode(hand.node);
        device.TryGetFeatureValue(CommonUsages.trigger, out float trigger);
        device.TryGetFeatureValue(CommonUsages.grip, out float grip);
        bool indexTouch = Touched(device, TriggerTouch) || Touched(device, IndexTouch);
        bool thumbTouch = Touched(device, CommonUsages.primaryTouch) || Touched(device, CommonUsages.secondaryTouch) ||
                          Touched(device, CommonUsages.primary2DAxisTouch) || Touched(device, ThumbTouch) ||
                          Touched(device, ThumbrestTouch);

        hand.index.target = Mathf.Lerp(indexTouch ? 0.35f : 0f, 1f, trigger);
        foreach (var f in hand.grip) f.target = Mathf.Lerp(0.15f, 1f, grip);
        hand.thumb.target = thumbTouch ? 1f : 0.2f;
    }

    static bool Touched(InputDevice device, InputFeatureUsage<bool> usage) =>
        device.TryGetFeatureValue(usage, out bool value) && value;

    static void Pose(Finger finger)
    {
        for (int i = 0; i < finger.joints.Length; i++)
        {
            var bend = Quaternion.AngleAxis(Mathf.Lerp(finger.open[i], finger.closed[i], finger.curl), finger.axis);
            finger.joints[i].localRotation = i == 0 ? finger.baseRotation * bend : bend;
        }
    }

    // Controller poses are relative to the tracking space, which the gloves are parented to. Also run right
    // before rendering, so the gloves use the newest pose (less lag when moving the hands fast).
    void UpdatePoses()
    {
        if (hands == null || preview) return;

        foreach (var hand in hands)
        {
            var device = InputDevices.GetDeviceAtXRNode(hand.node);
            bool tracked = false;
            if (device.isValid &&
                !(device.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && !isTracked) &&
                device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) &&
                device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation))
            {
                hand.root.localPosition = position;
                hand.root.localRotation = rotation;
                tracked = true;
            }
            // hidden while the controller is lost or put down, instead of floating frozen in place
            if (hand.root.gameObject.activeSelf != tracked) hand.root.gameObject.SetActive(tracked);
        }
    }

    // ---------------------------------------------------------------- building

    void Build(Transform parent, bool previewMode)
    {
        preview = previewMode;
        armor = Queued(armorMaterial);
        joint = Queued(jointMaterial);
        glow = Queued(glowMaterial);

        hands = new[] { BuildHand(parent, XRNode.LeftHand, -1f), BuildHand(parent, XRNode.RightHand, 1f) };

        if (preview)
        {
            // roughly where hands holding the controllers would be, seen from the pilot's eyes
            var tube = new Vector3(0f, 0.8f, 0.6f);   // fist axis: up and forward
            var wrist = new Vector3(0f, 0.6f, -0.8f); // toward the wrist
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                hands[i].root.localPosition = new Vector3(0.15f * s, -0.16f, 0.38f);
                hands[i].root.localRotation = Quaternion.LookRotation(tube, wrist);
            }
        }
    }

    static Material Queued(Material source)
    {
        var m = source != null ? new Material(source) : null;
        if (m != null) m.renderQueue = HandQueue;
        return m;
    }

    Hand BuildHand(Transform parent, XRNode node, float s)
    {
        var hand = new Hand { node = node };
        hand.root = new GameObject(node == XRNode.LeftHand ? "LeftGlove" : "RightGlove").transform;
        hand.root.SetParent(parent, false);

        // fine-tuning offsets, mirrored across X for the left hand
        var model = new GameObject("Model").transform;
        model.SetParent(hand.root, false);
        model.localPosition = new Vector3(positionOffset.x * s, positionOffset.y, positionOffset.z);
        model.localRotation = Quaternion.Euler(rotationOffset.x, rotationOffset.y * s, rotationOffset.z * s);

        // palm and back of the hand, beside the fist (+X for the right hand), wrist toward +Y, knuckles toward -Y
        Part(model, PrimitiveType.Cube, armor, new Vector3(0.031f * s, 0.012f, -0.002f), new Vector3(0.022f, 0.072f, 0.08f));
        Part(model, PrimitiveType.Cube, armor, new Vector3(0.043f * s, 0.006f, -0.002f), new Vector3(0.006f, 0.058f, 0.068f));
        Part(model, PrimitiveType.Cube, glow, new Vector3(0.0465f * s, 0.006f, -0.002f), new Vector3(0.002f, 0.044f, 0.005f));
        Part(model, PrimitiveType.Cube, glow, new Vector3(0.0465f * s, -0.018f, 0.018f), new Vector3(0.002f, 0.004f, 0.016f));
        Part(model, PrimitiveType.Cube, glow, new Vector3(0.0465f * s, -0.018f, -0.022f), new Vector3(0.002f, 0.004f, 0.016f));

        // knuckle guard over the base of the fingers
        Part(model, PrimitiveType.Cube, armor, new Vector3(0.036f * s, -0.024f, -0.001f), new Vector3(0.018f, 0.014f, 0.078f));

        // cuff around the wrist: dark sleeve, glowing ring, armored flare
        // (flattened like a real wrist: thin across the palm, wide from thumb side to little finger side)
        // the sleeve runs past the flare so the end facing the pilot is matte, not a shiny metal disc
        Part(model, PrimitiveType.Cylinder, joint, new Vector3(0.031f * s, 0.082f, -0.004f), new Vector3(0.036f, 0.032f, 0.06f));
        Part(model, PrimitiveType.Cylinder, glow, new Vector3(0.031f * s, 0.058f, -0.004f), new Vector3(0.0385f, 0.0016f, 0.0625f));
        Part(model, PrimitiveType.Cylinder, armor, new Vector3(0.031f * s, 0.084f, -0.004f), new Vector3(0.042f, 0.007f, 0.066f));

        // fingers start at the knuckles and curl around the fist (toward -X for the right hand)
        Vector3 forward = Vector3.down;
        Vector3 wrap = new Vector3(-s, 0f, 0f);
        float[] fingerOpen = { 4f, 6f, 4f };
        float[] fingerClosed = { 80f, 95f, 70f };
        hand.index = MakeFinger(model, s, "Index", new Vector3(0.028f * s, -0.028f, 0.029f), forward, wrap,
                                new[] { 0.038f, 0.024f, 0.02f }, 0.017f, fingerOpen, fingerClosed);
        hand.grip = new[]
        {
            MakeFinger(model, s, "Middle", new Vector3(0.028f * s, -0.03f, 0.009f), forward, wrap,
                       new[] { 0.042f, 0.027f, 0.021f }, 0.017f, fingerOpen, fingerClosed),
            MakeFinger(model, s, "Ring", new Vector3(0.028f * s, -0.029f, -0.011f), forward, wrap,
                       new[] { 0.039f, 0.025f, 0.02f }, 0.016f, fingerOpen, fingerClosed),
            MakeFinger(model, s, "Pinky", new Vector3(0.027f * s, -0.025f, -0.029f), forward, wrap,
                       new[] { 0.031f, 0.019f, 0.017f }, 0.014f, fingerOpen, fingerClosed),
        };

        // thumb: from the side of the palm near the index, along the top of the fist; it presses down (-Z)
        // onto the face buttons
        Vector3 thumbDirection = new Vector3(-0.3f * s, -0.7f, 0.65f).normalized;
        hand.thumb = MakeFinger(model, s, "Thumb", new Vector3(0.024f * s, -0.002f, 0.044f), thumbDirection, Vector3.back,
                                new[] { 0.034f, 0.027f, 0.021f }, 0.019f, new[] { 0f, 8f, 4f }, new[] { 30f, 35f, 25f });

        return hand;
    }

    // A chain of joints. Each segment hangs along the joint's local -Y; the joints bend around one axis, chosen
    // so that bending moves the finger from `direction` toward `bendToward` (both in hand space).
    Finger MakeFinger(Transform parent, float s, string name, Vector3 knuckle, Vector3 direction, Vector3 bendToward,
                      float[] lengths, float width, float[] open, float[] closed)
    {
        direction.Normalize();
        var finger = new Finger
        {
            joints = new Transform[lengths.Length],
            baseRotation = Quaternion.FromToRotation(Vector3.down, direction),
            open = open,
            closed = closed,
        };
        // AngleAxis(angle, a) turns `direction` toward a x direction, so a = direction x bendToward
        Vector3 axisInHand = Vector3.Cross(direction, bendToward).normalized;
        finger.axis = Quaternion.Inverse(finger.baseRotation) * axisInHand;
        // the back of the finger (where its light sits) faces the back of the hand
        Vector3 back = Quaternion.Inverse(finger.baseRotation) * new Vector3(s, 0f, 0f);
        back = Vector3.ProjectOnPlane(back, Vector3.up).normalized;

        Transform current = parent;
        Vector3 position = knuckle;
        for (int i = 0; i < lengths.Length; i++)
        {
            float length = lengths[i];
            float w = width * (1f - 0.08f * i); // a little thinner toward the tip

            var j = new GameObject(name + i).transform;
            j.SetParent(current, false);
            j.localPosition = position;
            finger.joints[i] = j;

            Part(j, PrimitiveType.Sphere, joint, Vector3.zero, Vector3.one * w * 1.02f);
            Part(j, PrimitiveType.Capsule, armor, new Vector3(0f, -length * 0.5f, 0f), new Vector3(w, length * 0.5f, w));

            if (i == lengths.Length - 1)
            {
                // light at the fingertip, on the back like a nail
                Part(j, PrimitiveType.Cube, glow, back * w * 0.42f + new Vector3(0f, -length * 0.62f, 0f),
                     new Vector3(w * 0.45f, w * 0.7f, w * 0.45f));
            }
            current = j;
            position = new Vector3(0f, -length, 0f);
        }

        finger.curl = finger.target = 0f;
        Pose(finger);
        return finger;
    }

    static void Part(Transform parent, PrimitiveType type, Material material, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = type.ToString();
        Destroy(go.GetComponent<Collider>()); // visual only: they must not block shots or the pointers

        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = position;
        t.localScale = scale;

        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (material != null) r.sharedMaterial = material;
    }
}
