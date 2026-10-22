using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Two cannons, one on each side of the ship. Hold to keep firing.
///
/// Desktop:  Left mouse button = left cannon, right mouse button = right cannon
///           (the mouse must be captured by the game window, same as for head look).
/// VR:       Left trigger = left cannon, right trigger = right cannon (the buttons on the back of the controllers).
/// Gamepad:  Left trigger / right trigger.
///
/// Three weapon types, chosen with the three switches on the left of the cockpit panel (VR: point and press A),
/// keys 4 / 5 / 6 or the gamepad d-pad left / right. Stronger weapons fire more slowly.
///
/// Bolts leave from each cannon and converge on a point ahead of the ship.
/// </summary>
public class ShipWeapons : MonoBehaviour
{
    [Header("Cannons")]
    [Tooltip("Muzzle positions, local to the ship (outside the cockpit, on each side).")]
    [SerializeField] Vector3 leftMuzzle = new Vector3(-1.3f, -0.2f, 0.8f);
    [SerializeField] Vector3 rightMuzzle = new Vector3(1.3f, -0.2f, 0.8f);

    [Header("Weapons")]
    [Tooltip("Index of the weapon selected at start.")]
    [SerializeField] int startWeapon = 0;
    [SerializeField] WeaponType[] weapons =
    {
        new WeaponType { name = "Blaster", damage = 1f,  fireInterval = 0.15f, speed = 90f, lifetime = 3f,
                         size = new Vector3(0.08f, 0.08f, 1.4f), color = new Color(0.2f, 0.9f, 1f),
                         soundStart = 1500f, soundEnd = 220f, soundDuration = 0.22f, pitch = 1f },
        new WeaponType { name = "Cannon",  damage = 3f,  fireInterval = 0.5f,  speed = 70f, lifetime = 3.5f,
                         size = new Vector3(0.18f, 0.18f, 2.2f), color = new Color(1f, 0.55f, 0.1f),
                         soundStart = 700f, soundEnd = 90f, soundDuration = 0.32f, pitch = 0.9f },
        new WeaponType { name = "Plasma",  damage = 8f,  fireInterval = 1.4f,  speed = 45f, lifetime = 5f,
                         size = new Vector3(0.6f, 0.6f, 0.6f), round = true, color = new Color(0.8f, 0.2f, 1f),
                         soundStart = 300f, soundEnd = 45f, soundDuration = 0.6f, pitch = 0.8f },
    };
    [SerializeField] Material boltMaterial;
    [Tooltip("Distance ahead at which the two cannons' shots cross.")]
    [SerializeField] float convergeDistance = 60f;

    [Header("Sound")]
    [Range(0f, 1f)] [SerializeField] float shotVolume = 0.5f;
    [Tooltip("Random pitch variation per shot, so rapid fire doesn't sound mechanical.")]
    [SerializeField] float pitchVariation = 0.08f;

    [System.Serializable]
    public class WeaponType
    {
        public string name = "Weapon";
        [Tooltip("Health removed from whatever the bolt hits.")]
        public float damage = 1f;
        [Tooltip("Seconds between shots of each cannon. Stronger weapons should be slower.")]
        public float fireInterval = 0.15f;
        public float speed = 90f;
        public float lifetime = 3f;
        public Vector3 size = new Vector3(0.08f, 0.08f, 1.4f);
        [Tooltip("Round bolt (sphere) instead of a long one (cube).")]
        public bool round;
        [Tooltip("Bolt tint and the color the cockpit switch lights up with.")]
        public Color color = Color.white;
        [Tooltip("Optional: replace the generated shot sound with your own clip.")]
        public AudioClip clip;
        [Header("Generated sound")]
        public float soundStart = 1500f;
        public float soundEnd = 220f;
        public float soundDuration = 0.22f;
        public float pitch = 1f;
    }

    InputAction fireLeft, fireRight;
    InputAction[] selectKeys;
    InputAction nextWeapon, previousWeapon;
    AudioSource leftAudio, rightAudio;
    MaterialPropertyBlock boltBlock;
    int current;
    float nextLeft, nextRight;
    bool vr;
    bool wasCaptured;
    Transform aim;

    void Awake()
    {
        vr = XRSettings.isDeviceActive;

        fireLeft = new InputAction("FireLeft", InputActionType.Button);
        fireLeft.AddBinding("<Mouse>/leftButton");
        fireLeft.AddBinding("<Gamepad>/leftTrigger");

        fireRight = new InputAction("FireRight", InputActionType.Button);
        fireRight.AddBinding("<Mouse>/rightButton");
        fireRight.AddBinding("<Gamepad>/rightTrigger");

        selectKeys = new[]
        {
            new InputAction("Weapon1", InputActionType.Button, "<Keyboard>/4"),
            new InputAction("Weapon2", InputActionType.Button, "<Keyboard>/5"),
            new InputAction("Weapon3", InputActionType.Button, "<Keyboard>/6"),
        };
        nextWeapon = new InputAction("NextWeapon", InputActionType.Button, "<Gamepad>/dpad/right");
        previousWeapon = new InputAction("PreviousWeapon", InputActionType.Button, "<Gamepad>/dpad/left");

        boltBlock = new MaterialPropertyBlock();
        foreach (var w in weapons)
            if (w.clip == null) w.clip = CreateLaserClip(w.soundStart, w.soundEnd, w.soundDuration);
        current = Mathf.Clamp(startWeapon, 0, weapons.Length - 1);

        leftAudio = CreateAudioSource("LeftCannonAudio", leftMuzzle);
        rightAudio = CreateAudioSource("RightCannonAudio", rightMuzzle);
    }

    AudioSource CreateAudioSource(string objectName, Vector3 localPosition)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0.7f; // mostly 3D so left/right cannons sound left/right
        source.minDistance = 2f;
        source.volume = shotVolume;
        return source;
    }

    // Synthesized "pew": a fast downward frequency sweep with a sharp attack and exponential decay.
    static AudioClip CreateLaserClip(float startFreq, float endFreq, float duration)
    {
        const int sampleRate = 44100;

        int count = (int)(sampleRate * duration);
        var samples = new float[count];
        float phase = 0f;

        for (int i = 0; i < count; i++)
        {
            float t = i / (float)count; // 0..1
            float freq = startFreq * Mathf.Pow(endFreq / startFreq, t);
            phase += 2f * Mathf.PI * freq / sampleRate;

            // sine plus a little square-ish edge for a more "electronic" tone
            float wave = Mathf.Sin(phase) * 0.8f + Mathf.Sign(Mathf.Sin(phase * 0.5f)) * 0.12f;

            float attack = Mathf.Clamp01(i / (sampleRate * 0.004f));
            float decay = Mathf.Exp(-t * 6f);
            samples[i] = wave * attack * decay * 0.8f;
        }

        var clip = AudioClip.Create("Shot", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    public WeaponType Weapon => weapons[current];
    public int WeaponCount => weapons.Length;
    public int CurrentWeapon => current;

    public void SelectWeapon(int index)
    {
        current = Mathf.Clamp(index, 0, weapons.Length - 1);
    }

    public Color WeaponColor(int index) => weapons[Mathf.Clamp(index, 0, weapons.Length - 1)].color;

    void PlayShot(AudioSource source)
    {
        source.pitch = Weapon.pitch + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(Weapon.clip, 1f);
    }

    void Start()
    {
        // VR: shoot where you look. Desktop: the head only looks around, shots follow the ship.
        aim = vr && Camera.main != null ? Camera.main.transform : transform;
    }

    void OnEnable()
    {
        fireLeft.Enable();
        fireRight.Enable();
        foreach (var k in selectKeys) k.Enable();
        nextWeapon.Enable();
        previousWeapon.Enable();
    }

    void OnDisable()
    {
        fireLeft.Disable();
        fireRight.Disable();
        foreach (var k in selectKeys) k.Disable();
        nextWeapon.Disable();
        previousWeapon.Disable();
    }

    void Update()
    {
        if (!GameFlow.CanFire)
        {
            wasCaptured = false; // after a menu the mouse has to be captured again before it can shoot
            return;
        }

        for (int i = 0; i < selectKeys.Length && i < weapons.Length; i++)
            if (selectKeys[i].WasPressedThisFrame()) SelectWeapon(i);
        if (nextWeapon.WasPressedThisFrame()) SelectWeapon(current + 1);
        if (previousWeapon.WasPressedThisFrame()) SelectWeapon(current - 1);

        // Ignore mouse clicks until the game owns the mouse, so the click that captures it doesn't shoot.
        bool captured = vr || Cursor.lockState == CursorLockMode.Locked;
        bool canFire = captured && wasCaptured;
        wasCaptured = captured;
        if (!canFire) return;

        if ((fireLeft.IsPressed() || TriggerPressed(XRNode.LeftHand)) && Time.time >= nextLeft)
        {
            nextLeft = Time.time + Weapon.fireInterval;
            Fire(leftMuzzle);
            PlayShot(leftAudio);
        }

        if ((fireRight.IsPressed() || TriggerPressed(XRNode.RightHand)) && Time.time >= nextRight)
        {
            nextRight = Time.time + Weapon.fireInterval;
            Fire(rightMuzzle);
            PlayShot(rightAudio);
        }
    }

    // Legacy XR InputDevices API: works without enabling an OpenXR interaction profile.
    static bool TriggerPressed(XRNode node)
    {
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (!device.isValid) return false;
        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float value)) return value > 0.5f;
        return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool button) && button;
    }

    void Fire(Vector3 localMuzzle)
    {
        Vector3 origin = transform.TransformPoint(localMuzzle);
        Vector3 target = aim.position + aim.forward * convergeDistance;
        Vector3 direction = (target - origin).normalized;

        var weapon = Weapon;
        var bolt = GameObject.CreatePrimitive(weapon.round ? PrimitiveType.Sphere : PrimitiveType.Cube);
        bolt.name = "Bolt";
        Destroy(bolt.GetComponent<Collider>());

        var t = bolt.transform;
        t.position = origin;
        t.localScale = weapon.size;

        var r = bolt.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (boltMaterial != null) r.sharedMaterial = boltMaterial;

        // tint per weapon (works with URP Lit/Unlit and the built-in shaders)
        boltBlock.SetColor("_BaseColor", weapon.color);
        boltBlock.SetColor("_Color", weapon.color);
        boltBlock.SetColor("_EmissionColor", weapon.color * 2f);
        r.SetPropertyBlock(boltBlock);

        bolt.AddComponent<Projectile>().Launch(direction, weapon.speed, weapon.lifetime, weapon.damage, transform);
    }
}
