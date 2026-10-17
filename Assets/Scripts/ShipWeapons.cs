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
/// Bolts leave from each cannon and converge on a point ahead of the ship.
/// </summary>
public class ShipWeapons : MonoBehaviour
{
    [Header("Cannons")]
    [Tooltip("Muzzle positions, local to the ship (outside the cockpit, on each side).")]
    [SerializeField] Vector3 leftMuzzle = new Vector3(-1.3f, -0.2f, 0.8f);
    [SerializeField] Vector3 rightMuzzle = new Vector3(1.3f, -0.2f, 0.8f);
    [SerializeField] float fireInterval = 0.15f;

    [Header("Bolt")]
    [SerializeField] float boltSpeed = 90f;
    [SerializeField] float boltLifetime = 3f;
    [Tooltip("Health removed from whatever the bolt hits.")]
    [SerializeField] float boltDamage = 1f;
    [SerializeField] Vector3 boltSize = new Vector3(0.08f, 0.08f, 1.4f);
    [SerializeField] Material boltMaterial;
    [Tooltip("Distance ahead at which the two cannons' shots cross.")]
    [SerializeField] float convergeDistance = 60f;

    [Header("Sound")]
    [Tooltip("Optional: replace the generated shot sound with your own clip.")]
    [SerializeField] AudioClip shotClip;
    [Range(0f, 1f)] [SerializeField] float shotVolume = 0.5f;
    [Tooltip("Random pitch variation per shot, so rapid fire doesn't sound mechanical.")]
    [SerializeField] float pitchVariation = 0.08f;

    InputAction fireLeft, fireRight;
    AudioSource leftAudio, rightAudio;
    float nextLeft, nextRight;
    bool vr;
    bool wasCaptured;
    Transform aim;

    void Awake()
    {
        vr = XRSettings.isDeviceActive;

        fireLeft = new InputAction("FireLeft", InputActionType.Button);
        fireLeft.AddBinding("<Mouse>/leftButton");
        fireLeft.AddBinding("<XRController>{LeftHand}/triggerPressed");
        fireLeft.AddBinding("<Gamepad>/leftTrigger");

        fireRight = new InputAction("FireRight", InputActionType.Button);
        fireRight.AddBinding("<Mouse>/rightButton");
        fireRight.AddBinding("<XRController>{RightHand}/triggerPressed");
        fireRight.AddBinding("<Gamepad>/rightTrigger");

        if (shotClip == null) shotClip = CreateLaserClip();
        leftAudio = CreateAudioSource("LeftCannonAudio", leftMuzzle);
        rightAudio = CreateAudioSource("RightCannonAudio", rightMuzzle);
    }

    AudioSource CreateAudioSource(string objectName, Vector3 localPosition)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPosition;

        var source = go.AddComponent<AudioSource>();
        source.clip = shotClip;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0.7f; // mostly 3D so left/right cannons sound left/right
        source.minDistance = 2f;
        source.volume = shotVolume;
        return source;
    }

    // Synthesized "pew": a fast downward frequency sweep with a sharp attack and exponential decay.
    static AudioClip CreateLaserClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.22f;
        const float startFreq = 1500f;
        const float endFreq = 220f;

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

        var clip = AudioClip.Create("LaserShot", count, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    void PlayShot(AudioSource source)
    {
        source.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        source.PlayOneShot(shotClip, 1f);
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
    }

    void OnDisable()
    {
        fireLeft.Disable();
        fireRight.Disable();
    }

    void Update()
    {
        // Ignore mouse clicks until the game owns the mouse, so the click that captures it doesn't shoot.
        bool captured = vr || Cursor.lockState == CursorLockMode.Locked;
        bool canFire = captured && wasCaptured;
        wasCaptured = captured;
        if (!canFire) return;

        if (fireLeft.IsPressed() && Time.time >= nextLeft)
        {
            nextLeft = Time.time + fireInterval;
            Fire(leftMuzzle);
            PlayShot(leftAudio);
        }

        if (fireRight.IsPressed() && Time.time >= nextRight)
        {
            nextRight = Time.time + fireInterval;
            Fire(rightMuzzle);
            PlayShot(rightAudio);
        }
    }

    void Fire(Vector3 localMuzzle)
    {
        Vector3 origin = transform.TransformPoint(localMuzzle);
        Vector3 target = aim.position + aim.forward * convergeDistance;
        Vector3 direction = (target - origin).normalized;

        var bolt = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bolt.name = "Bolt";
        Destroy(bolt.GetComponent<Collider>());

        var t = bolt.transform;
        t.position = origin;
        t.localScale = boltSize;

        var r = bolt.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        if (boltMaterial != null) r.sharedMaterial = boltMaterial;

        bolt.AddComponent<Projectile>().Launch(direction, boltSpeed, boltLifetime, boltDamage, transform);
    }
}
