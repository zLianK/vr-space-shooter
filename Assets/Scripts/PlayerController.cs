using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

/// <summary>
/// Zero-gravity movement for the player sphere. The same actions are bound to both
/// keyboard/mouse (desktop simulation) and the XR controllers, so it works with or without a headset.
///
/// Desktop:  1/2/3 = throttle lever level, Left Shift = accelerate, W/S = nose up/down, A/D = turn left/right,
///           Q/E = strafe, X = brake, Mouse = head look (turns your head, not the ship),
///           C = recenter head, Esc = release the mouse, click = capture it again.
/// VR:       Right grip = accelerate (strength set by the lever), point the right controller at the
///           throttle lever, hold A and move it forward / back to change level, Left stick = move/strafe, Right stick Y = up / down,
///           Right stick X = turn, Left grip = brake. Head rotation is the look direction.
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
    [Tooltip("Pilot's eye position (local to the player) when playing without a headset: first person, inside the cockpit.")]
    [SerializeField] Vector3 desktopCameraOffset = new Vector3(0f, 0.1f, 0f);
    [SerializeField] Transform view;

    Rigidbody body;
    InputAction throttleUp, throttleDown, throttle1, throttle2, throttle3;
    public int ThrottleLevel { get; private set; } = 1;
    public int ThrottleLevels => throttleLevels;

    InputAction move, ascend, yaw, pitch, accelerate, mouseLook, recenter, brake;
    float headYaw, headPitch;
    bool vr;
    Vector2 vrMove;
    float vrUp, vrYaw, vrAccelerate;
    bool vrBrake;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.freezeRotation = true;

        if (view == null && Camera.main != null) view = Camera.main.transform;
        vr = XRSettings.isDeviceActive;
        SetupCamera();
        CreateActions();
    }

    void SetupCamera()
    {
        if (view == null) return;

        view.SetParent(transform, false);
        if (vr)
        {
            // the headset drives the camera pose, relative to the player
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
        }
        else
        {
            view.localPosition = desktopCameraOffset;
            view.localRotation = Quaternion.identity;
        }
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
    }

    void OnDisable()
    {
        throttleUp.Disable(); throttleDown.Disable(); throttle1.Disable(); throttle2.Disable(); throttle3.Disable(); move.Disable(); ascend.Disable(); yaw.Disable(); pitch.Disable(); accelerate.Disable();
        mouseLook.Disable(); recenter.Disable(); brake.Disable();

        if (!vr) Cursor.lockState = CursorLockMode.None;
    }

    void Update()
    {
        if (!GameFlow.IsPlaying) return; // menu, pause or result screen

        float dt = Time.deltaTime;

        ReadVR();
        if (throttleUp.WasPressedThisFrame()) SetThrottle(ThrottleLevel + 1);
        if (throttleDown.WasPressedThisFrame()) SetThrottle(ThrottleLevel - 1);
        if (throttle1.WasPressedThisFrame()) SetThrottle(1);
        if (throttle2.WasPressedThisFrame()) SetThrottle(2);
        if (throttle3.WasPressedThisFrame()) SetThrottle(3);
        float yawDelta = Mathf.Clamp(yaw.ReadValue<float>() + vrYaw, -1f, 1f) * turnSpeed * dt;

        if (vr)
        {
            // in VR the head provides pitch/roll, so the body only turns around the vertical axis
            transform.Rotate(0f, yawDelta, 0f, Space.World);
            return;
        }

        float pitchDelta = -pitch.ReadValue<float>() * turnSpeed * dt;

        // rotate around the ship's own axes so pitch and yaw have no limits (loops, barrel turns, etc.)
        transform.Rotate(pitchDelta, yawDelta, 0f, Space.Self);

        UpdateHeadLook();
    }

    // Controllers are read through the legacy XR InputDevices API, which works with any OpenXR runtime
    // without needing an interaction profile enabled for the Input System layouts.
    void ReadVR()
    {
        vrMove = Vector2.zero; vrUp = vrYaw = vrAccelerate = 0f; vrBrake = false;
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
            if (right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 stick))
            {
                stick = Deadzone(stick);
                vrYaw = stick.x;
                vrUp = stick.y;
            }
            if (right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float rg)) vrAccelerate = rg;
        }
    }

    public void SetThrottle(int level)
    {
        ThrottleLevel = Mathf.Clamp(level, 1, throttleLevels);
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
        Vector2 m = Vector2.ClampMagnitude(move.ReadValue<Vector2>() + vrMove, 1f);
        float up = Mathf.Clamp(ascend.ReadValue<float>() + vrUp, -1f, 1f);

        // VR: thrust follows the head. Desktop: the head only looks around, thrust follows the ship.
        Transform reference = vr && view != null ? view : transform;
        float boost = Mathf.Clamp01(accelerate.ReadValue<float>() + vrAccelerate);
        float forward = boost * ThrottleLevel / throttleLevels;
        Vector3 wish = reference.right * m.x + reference.forward * (m.y + forward) + reference.up * up;
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
