using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Serialization;
using UnityEngine.XR;

/// <summary>
/// Zero-gravity movement for the player sphere. The same actions are bound to both
/// keyboard/mouse (desktop simulation) and the XR controllers, so it works with or without a headset.
///
/// Desktop:  1/2/3 = throttle lever level, Left Shift = accelerate, W/S = nose up/down, A/D = turn left/right
///           (the cockpit flight stick moves along: W pulls it back, S pushes it forward, A/D tilt it to the side),
///           Q/E = strafe, X = brake, Mouse = head look (turns your head, not the ship),
///           C = recenter head, Esc = release the mouse, click = capture it again.
/// VR:       Right grip = accelerate (strength set by the lever), point the right controller at the
///           throttle lever, hold A and move it forward / back to change level, Left stick = move/strafe,
///           Flight stick (in the cockpit, between the knees): put either hand on it, hold that hand's A / X and
///           move the hand: forward = nose down, back = nose up, to the side = turn, diagonals mix both.
///           Let go of the button to release it (it springs back to center). Without a stick in the cockpit
///           the right thumbstick steers instead. Left grip = brake. The head only looks around:
///           thrust and shots always follow the ship's nose.
///           The headset is recentered on the pilot's seat at start (and with C or the runtime's own recenter).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float thrust = 12f;
    [SerializeField] float maxSpeed = 25f;
    [SerializeField] float brakeDeceleration = 8f;
    [Tooltip("Deceleration with no movement input. Higher = shorter glide, 0 = drifts forever.")]
    [SerializeField] float idleDeceleration = 30f;

    [Header("Throttle lever")]
    [Tooltip("Positions of the right lever. Level N accelerates up to N/levels of maxSpeed, with N/levels of the thrust.")]
    [SerializeField] int throttleLevels = 3;

    [Header("Look")]
    [SerializeField] float turnSpeed = 90f;
    [Tooltip("Head look (mouse) sensitivity, in degrees per pixel.")]
    [SerializeField] float mouseSensitivity = 0.15f;
    [Tooltip("How far the head can turn to each side, in degrees.")]
    [SerializeField] float maxHeadYaw = 100f;
    [Tooltip("How far the head can tilt up and down, in degrees.")]
    [SerializeField] float maxHeadPitch = 60f;

    [Header("Camera")]
    [Tooltip("Pilot's eye position (local to the player): first person, inside the cockpit. " +
             "In VR the headset is recentered onto this point, facing the ship's nose.")]
    [FormerlySerializedAs("desktopCameraOffset")]
    [SerializeField] Vector3 eyePosition = new Vector3(0f, 0.1f, 0f);
    [SerializeField] Transform view;

    /// <summary>
    /// VR: parent of the camera whose space the headset and controller poses are reported in.
    /// It is offset so the tracked head lands on the pilot's eye. Desktop: the player itself.
    /// </summary>
    public Transform TrackingSpace { get; private set; }

    /// <summary>Raised after the headset is recentered (things placed in front of the head can move along).</summary>
    public static event System.Action Recentered;

    Rigidbody body;
    InputAction throttleUp, throttleDown, throttle1, throttle2, throttle3;
    public int ThrottleLevel { get; private set; } = 1;
    public int ThrottleLevels => throttleLevels;

    /// <summary>
    /// Flight stick deflection that steers the ship, -1..1 per axis: x = tilted right (turn right),
    /// y = pushed forward (nose down). Comes from the keys / gamepad or from the hand holding the cockpit stick.
    /// </summary>
    public Vector2 Stick { get; private set; }

    /// <summary>Set by <see cref="ShipVisual"/> when the cockpit has a stick: in VR the ship then only turns while a hand holds it.</summary>
    public bool HasCockpitStick { get; set; }

    InputAction move, ascend, yaw, pitch, accelerate, mouseLook, recenter, brake;
    float headYaw, headPitch;
    float yawInput, pitchInput;
    bool vr;
    Vector2 vrMove, vrStick;
    Vector2? heldStick;
    float vrAccelerate;
    bool vrBrake;
    bool recenterPending;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.freezeRotation = true;
        // physics runs at 50 Hz but a headset draws at 80-90 Hz: without interpolation the cockpit (and the
        // camera inside it) moves in steps, which judders in VR
        body.interpolation = RigidbodyInterpolation.Interpolate;

        if (view == null && Camera.main != null) view = Camera.main.transform;
        vr = XRSettings.isDeviceActive;
        SetupCamera();
        CreateActions();
    }

    void SetupCamera()
    {
        TrackingSpace = transform;
        if (view == null) return;

        // the default near plane (0.3 m) cuts away anything closer, like the pilot's hands and the cockpit
        var cam = view.GetComponent<Camera>();
        if (cam != null) cam.nearClipPlane = 0.02f;

        if (vr)
        {
            // The headset drives the camera pose relative to the tracking space. Its origin is the runtime's
            // (often the floor, facing the Guardian's forward), so RecenterHead moves it under the pilot's eye.
            TrackingSpace = new GameObject("TrackingSpace").transform;
            TrackingSpace.SetParent(transform, false);
            view.SetParent(TrackingSpace, false);
            view.localPosition = Vector3.zero;
            view.localRotation = Quaternion.identity;
            if (view.GetComponent<TrackedPoseDriver>() == null)
            {
                var driver = view.gameObject.AddComponent<TrackedPoseDriver>();
                driver.enabled = false;
                driver.positionInput = new InputActionProperty(new InputAction("HeadPosition", binding: "<XRHMD>/centerEyePosition"));
                driver.rotationInput = new InputActionProperty(new InputAction("HeadRotation", binding: "<XRHMD>/centerEyeRotation"));
                driver.enabled = true;
            }
            recenterPending = true;
        }
        else
        {
            view.SetParent(transform, false);
            view.localPosition = eyePosition;
            view.localRotation = Quaternion.identity;
        }
    }

    // The runtime's own recenter (e.g. holding the Oculus button) moves the tracking origin, so redo ours.
    void OnTrackingOriginUpdated(XRInputSubsystem _) => recenterPending = true;

    // Puts the tracked head on the pilot's eye, facing the ship's nose. Only yaw is corrected:
    // up stays the real up, so tilting the head still tilts the view.
    bool RecenterHead()
    {
        var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!head.isValid) return false;
        // the device can exist a few frames before it tracks: a pose read then is just zeros
        if (head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) && !tracked) return false;
        if (!head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out Vector3 position) &&
            !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out position))
            return false;
        if (!head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out Quaternion rotation) &&
            !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out rotation))
            return false;

        Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) return false; // looking straight up or down: wait for a usable heading

        Quaternion correction = Quaternion.Inverse(Quaternion.LookRotation(forward, Vector3.up));
        TrackingSpace.localRotation = correction;
        TrackingSpace.localPosition = eyePosition - correction * position;
        Recentered?.Invoke();
        return true;
    }

    void CreateActions()
    {
        move = new InputAction("Move", InputActionType.Value);
        move.AddCompositeBinding("2DVector")
            .With("Left", "<Keyboard>/q").With("Right", "<Keyboard>/e");
        move.AddBinding("<Gamepad>/leftStick");

        ascend = new InputAction("Ascend", InputActionType.Value);
        ascend.AddBinding("<Gamepad>/rightStick/y");

        yaw = new InputAction("Yaw", InputActionType.Value);
        yaw.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/a").With("Positive", "<Keyboard>/d");
        yaw.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/leftArrow").With("Positive", "<Keyboard>/rightArrow");
        yaw.AddBinding("<Gamepad>/rightStick/x");

        // positive = nose up
        pitch = new InputAction("Pitch", InputActionType.Value);
        pitch.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/s").With("Positive", "<Keyboard>/w");
        pitch.AddCompositeBinding("1DAxis")
            .With("Negative", "<Keyboard>/downArrow").With("Positive", "<Keyboard>/upArrow");

        accelerate = new InputAction("Accelerate", InputActionType.Value);
        accelerate.AddBinding("<Keyboard>/leftShift");
        // VR: triggers are the cannons (see ShipWeapons); the right grip accelerates, scaled by the throttle lever
        accelerate.AddBinding("<Gamepad>/rightShoulder");

        throttleUp = new InputAction("ThrottleUp", InputActionType.Button, "<Gamepad>/dpad/up");
        throttleDown = new InputAction("ThrottleDown", InputActionType.Button, "<Gamepad>/dpad/down");
        throttle1 = new InputAction("Throttle1", InputActionType.Button, "<Keyboard>/1");
        throttle2 = new InputAction("Throttle2", InputActionType.Button, "<Keyboard>/2");
        throttle3 = new InputAction("Throttle3", InputActionType.Button, "<Keyboard>/3");

        mouseLook = new InputAction("MouseLook", InputActionType.Value, "<Mouse>/delta");
        recenter = new InputAction("Recenter", InputActionType.Button, "<Keyboard>/c");

        brake = new InputAction("Brake", InputActionType.Button);
        brake.AddBinding("<Keyboard>/x");
        brake.AddBinding("<Gamepad>/buttonSouth");
    }

    void OnEnable()
    {
        throttleUp.Enable(); throttleDown.Enable(); throttle1.Enable(); throttle2.Enable(); throttle3.Enable(); move.Enable(); ascend.Enable(); yaw.Enable(); pitch.Enable(); accelerate.Enable();
        mouseLook.Enable(); recenter.Enable(); brake.Enable();

        if (!vr) Cursor.lockState = CursorLockMode.Locked;
        foreach (var s in InputSubsystems()) s.trackingOriginUpdated += OnTrackingOriginUpdated;
    }

    void OnDisable()
    {
        throttleUp.Disable(); throttleDown.Disable(); throttle1.Disable(); throttle2.Disable(); throttle3.Disable(); move.Disable(); ascend.Disable(); yaw.Disable(); pitch.Disable(); accelerate.Disable();
        mouseLook.Disable(); recenter.Disable(); brake.Disable();

        if (!vr) Cursor.lockState = CursorLockMode.None;
        foreach (var s in InputSubsystems()) s.trackingOriginUpdated -= OnTrackingOriginUpdated;
    }

    static List<XRInputSubsystem> InputSubsystems()
    {
        var list = new List<XRInputSubsystem>();
        SubsystemManager.GetSubsystems(list);
        return list;
    }

    void Update()
    {
        // VR: C (keyboard) also recenters the headset, in menus too
        if (vr && recenter.WasPressedThisFrame()) recenterPending = true;
        if (vr && recenterPending && TrackingSpace != transform) recenterPending = !RecenterHead();

        // the hand on the cockpit stick reports every frame it holds it (see HoldStick)
        Vector2? held = heldStick;
        heldStick = null;

        if (!GameFlow.IsPlaying) // menu, pause or result screen
        {
            Stick = Vector2.zero;
            return;
        }

        ReadVR();
        if (held.HasValue) vrStick = held.Value;
        if (throttleUp.WasPressedThisFrame()) SetThrottle(ThrottleLevel + 1);
        if (throttleDown.WasPressedThisFrame()) SetThrottle(ThrottleLevel - 1);
        if (throttle1.WasPressedThisFrame()) SetThrottle(1);
        if (throttle2.WasPressedThisFrame()) SetThrottle(2);
        if (throttle3.WasPressedThisFrame()) SetThrottle(3);

        // W (nose up) pulls the stick back, S pushes it forward
        Stick = new Vector2(Mathf.Clamp(yaw.ReadValue<float>() + vrStick.x, -1f, 1f),
                            Mathf.Clamp(-pitch.ReadValue<float>() + vrStick.y, -1f, 1f));
        // applied in FixedUpdate, together with the movement, so the interpolated body turns smoothly
        yawInput = Stick.x;
        pitchInput = Stick.y;

        if (!vr) UpdateHeadLook(); // in VR the headset moves the head
    }

    // Controllers are read through the legacy XR InputDevices API, which works with any OpenXR runtime
    // without needing an interaction profile enabled for the Input System layouts.
    void ReadVR()
    {
        vrMove = vrStick = Vector2.zero; vrAccelerate = 0f; vrBrake = false;
        if (!vr) return;

        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (left.isValid)
        {
            if (left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick)) vrMove = Deadzone(stick);
            if (left.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float lg)) vrBrake = lg > 0.5f;
        }
        if (right.isValid)
        {
            // no stick in the cockpit to grab: the thumbstick is the flight stick (push forward = nose down)
            if (!HasCockpitStick && right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick))
                vrStick = Deadzone(stick);
            if (right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float rg)) vrAccelerate = rg;
        }
    }

    public void SetThrottle(int level)
    {
        ThrottleLevel = Mathf.Clamp(level, 1, throttleLevels);
    }

    /// <summary>VR: called every frame while a hand holds the cockpit stick (x = right, y = forward). Not calling it lets go.</summary>
    public void HoldStick(Vector2 deflection)
    {
        heldStick = Deadzone(Vector2.ClampMagnitude(deflection, 1f));
    }

    static Vector2 Deadzone(Vector2 v) => v.magnitude > 0.15f ? v : Vector2.zero;

    // The mouse simulates the pilot's head: look around inside the cockpit without turning the ship.
    void UpdateHeadLook()
    {
        if (view == null) return;

        if (Cursor.lockState != CursorLockMode.Locked)
        {
            // click inside the game window to capture the mouse again (Esc releases it)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                Cursor.lockState = CursorLockMode.Locked;
            return;
        }

        if (recenter.WasPressedThisFrame())
        {
            headYaw = 0f;
            headPitch = 0f;
        }

        Vector2 delta = mouseLook.ReadValue<Vector2>() * mouseSensitivity;
        headYaw = Mathf.Clamp(headYaw + delta.x, -maxHeadYaw, maxHeadYaw);
        headPitch = Mathf.Clamp(headPitch - delta.y, -maxHeadPitch, maxHeadPitch);
        view.localRotation = Quaternion.Euler(headPitch, headYaw, 0f);
    }

    void FixedUpdate()
    {
        // rotate around the ship's own axes so pitch and yaw have no limits (loops, barrel turns, etc.)
        float step = turnSpeed * Time.fixedDeltaTime;
        if (GameFlow.IsPlaying) body.MoveRotation(body.rotation * Quaternion.Euler(pitchInput * step, yawInput * step, 0f));

        Vector2 m = Vector2.ClampMagnitude(move.ReadValue<Vector2>() + vrMove, 1f);
        float up = Mathf.Clamp(ascend.ReadValue<float>(), -1f, 1f);

        // thrust always follows the ship; the head (VR or mouse look) only looks around.
        // body.rotation, not transform: with interpolation the transform lags a little behind the body
        Quaternion ship = body.rotation;
        float boost = Mathf.Clamp01(accelerate.ReadValue<float>() + vrAccelerate);
        float forward = boost * ThrottleLevel / throttleLevels;
        Vector3 wish = ship * Vector3.right * m.x + ship * Vector3.forward * (m.y + forward) + ship * Vector3.up * up;
        if (wish.sqrMagnitude > 1f) wish.Normalize();

        body.AddForce(wish * thrust, ForceMode.Acceleration);

        Vector3 velocity = body.linearVelocity;
        if (brake.IsPressed() || vrBrake)
        {
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, brakeDeceleration * Time.fixedDeltaTime);
        }
        else if (wish.sqrMagnitude < 0.0001f)
        {
            // no input: short glide before stopping
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, idleDeceleration * Time.fixedDeltaTime);
        }
        // while accelerating, the lever level caps the speed at its share of maxSpeed
        float cap = boost > 0f ? maxSpeed * ThrottleLevel / throttleLevels : maxSpeed;
        if (velocity.magnitude > cap)
            velocity = Vector3.MoveTowards(velocity, velocity.normalized * cap, brakeDeceleration * Time.fixedDeltaTime);
        body.linearVelocity = Vector3.ClampMagnitude(velocity, maxSpeed);
    }
}
