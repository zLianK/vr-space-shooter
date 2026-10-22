using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using XR = UnityEngine.XR;

/// <summary>
/// Match flow: start menu, pause menu (Esc / left controller menu button) and the result screen
/// when the 120 seconds run out. Creates itself when the scene loads, so it needs no scene setup.
///
/// The menus are simple 3D panels in front of the pilot, so they work in VR and on the desktop.
///   VR:       point a controller at a button and pull the trigger (or press A / X).
///   Desktop:  mouse click, or Up / Down + Enter.   Gamepad: d-pad + south button.
///
/// While the game is not running (menu, pause, result) time is stopped (Time.timeScale = 0) and the
/// ship controls, weapons and cockpit buttons are disabled.
/// </summary>
public class GameFlow : MonoBehaviour
{
    public enum State { Menu, Playing, Paused, Finished }

    /// <summary>Length of a match in seconds.</summary>
    public const float MatchSeconds = 120f;

    const float PanelDistance = 0.8f;   // meters in front of the pilot's eyes (inside the cockpit)
    const float PanelWidth = 0.7f;
    const float ButtonWidth = 0.54f;
    const float ButtonHeight = 0.085f;
    const float ButtonSpacing = 0.105f;

    static readonly Color PanelColor = new Color(0.03f, 0.05f, 0.09f);
    static readonly Color ButtonColor = new Color(0.12f, 0.2f, 0.32f);
    static readonly Color ButtonHoverColor = new Color(0.1f, 0.6f, 0.75f);
    static readonly Color TextColor = new Color(0.9f, 0.97f, 1f);
    static readonly Color TitleColor = new Color(0.3f, 1f, 0.9f);

    public static GameFlow Instance { get; private set; }
    public static State Current => Instance != null ? Instance.state : State.Playing;
    public static bool IsPlaying => Current == State.Playing;
    /// <summary>Seconds left in the match (full length until the match starts).</summary>
    public static float TimeLeft => Instance != null ? Instance.timeLeft : MatchSeconds;
    /// <summary>False while paused and for a moment after resuming, so the click on a button is not a shot.</summary>
    public static bool CanFire => IsPlaying && (Instance == null || Time.unscaledTime >= Instance.fireBlockedUntil);

    static bool skipMenu; // set by "Reiniciar" / "Jogar novamente": the reloaded scene starts playing right away

    State state;
    float timeLeft = MatchSeconds;
    float fireBlockedUntil;
    float placeUntil;
    bool menuWasDown;

    class Button
    {
        public string label;
        public Action action;
        public float y;
        public Renderer background;
    }

    Transform panel;
    readonly List<Button> buttons = new List<Button>();
    readonly bool[] handWasDown = new bool[2];
    Transform[] beams;
    int keyboardIndex;
    int hovered = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (Instance == null) new GameObject("GameFlow").AddComponent<GameFlow>();
    }

    void Awake()
    {
        Instance = this;
        Time.timeScale = 1f;

        // survives the scene reload done by "Reiniciar" / "Jogar novamente"
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        ShowMenu(State.Menu);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    // A restart reloads the scene: start a fresh match right away instead of going back to the start menu.
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!skipMenu) return;

        skipMenu = false;
        timeLeft = MatchSeconds;
        state = State.Playing;
        Time.timeScale = 1f;
        fireBlockedUntil = Time.unscaledTime + 0.4f;
        HideMenu();
    }

    // ---------------------------------------------------------------- flow

    void Update()
    {
        bool escape = EscapePressed();

        if (state == State.Playing)
        {
            if (escape) ShowMenu(State.Paused);

            timeLeft -= Time.deltaTime;
            if (timeLeft <= 0f)
            {
                timeLeft = 0f;
                ShowMenu(State.Finished);
            }
        }
        else if (state == State.Paused && escape)
        {
            Resume();
        }

        if (state != State.Playing) UpdateMenu();
        ApplyCursor();
    }

    void StartMatch()
    {
        timeLeft = MatchSeconds;
        Resume();
    }

    void Resume()
    {
        state = State.Playing;
        Time.timeScale = 1f;
        fireBlockedUntil = Time.unscaledTime + 0.4f;
        HideMenu();
    }

    void Restart()
    {
        skipMenu = true;
        HideMenu();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    bool EscapePressed()
    {
        // P is an alternative to Esc: the Unity editor takes Esc to release the mouse while the game has it captured
        bool pressed = Keyboard.current != null &&
                       (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame);
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame) pressed = true;

        // VR: menu button of the left controller
        bool menuDown = false;
        var left = XR.InputDevices.GetDeviceAtXRNode(XR.XRNode.LeftHand);
        if (left.isValid) left.TryGetFeatureValue(XR.CommonUsages.menuButton, out menuDown);
        if (menuDown && !menuWasDown) pressed = true;
        menuWasDown = menuDown;

        return pressed;
    }

    // Desktop: the mouse is free while a menu is open and captured while playing.
    void ApplyCursor()
    {
        if (XR.XRSettings.isDeviceActive) return;

        bool menu = state != State.Playing;
        Cursor.lockState = menu ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = menu;
    }

    // ---------------------------------------------------------------- menu content

    void ShowMenu(State newState)
    {
        state = newState;
        Time.timeScale = 0f;
        keyboardIndex = 0;
        hovered = -1;
        BuildPanel();
        placeUntil = Time.unscaledTime + 1f; // the headset may not be tracking yet on the very first frame
        PlacePanel();
    }

    void HideMenu()
    {
        if (panel != null) Destroy(panel.gameObject);
        panel = null;
        buttons.Clear();
        if (beams != null)
            foreach (var beam in beams) beam.gameObject.SetActive(false);
    }

    void BuildPanel()
    {
        HideMenu();
        panel = new GameObject("Menu").transform;
        panel.SetParent(transform, false);

        string title, subtitle = null;
        switch (state)
        {
            case State.Paused:
                title = "PAUSADO";
                buttons.Add(new Button { label = "Continuar", action = Resume });
                buttons.Add(new Button { label = "Reiniciar", action = Restart });
                buttons.Add(new Button { label = "Fechar", action = Quit });
                break;
            case State.Finished:
                title = "TEMPO ESGOTADO";
                subtitle = "Asteroides destruídos: " + SpaceDebris.DestroyedCount;
                buttons.Add(new Button { label = "Jogar novamente", action = Restart });
                buttons.Add(new Button { label = "Fechar", action = Quit });
                break;
            default:
                title = "VR SPACE SHOOTER";
                subtitle = "Destrua o máximo de asteroides em " + Mathf.RoundToInt(MatchSeconds) + " segundos";
                buttons.Add(new Button { label = "Iniciar", action = StartMatch });
                buttons.Add(new Button { label = "Fechar", action = Quit });
                break;
        }

        // layout from the top: title, optional line, buttons
        float y = 0.19f;
        float top = y + 0.08f;
        MakeText("Title", title, new Vector3(0f, y, -0.012f), 0.06f, TitleColor, PanelWidth * 0.9f);
        y -= 0.095f;
        if (subtitle != null)
        {
            MakeText("Subtitle", subtitle, new Vector3(0f, y, -0.012f), 0.035f, TextColor, PanelWidth * 0.9f);
            y -= 0.085f;
        }
        else
        {
            y -= 0.02f;
        }

        foreach (var b in buttons)
        {
            b.y = y;
            var quad = MakeBox("Button", new Vector3(0f, y, -0.006f), new Vector3(ButtonWidth, ButtonHeight, 0.01f), ButtonColor);
            b.background = quad.GetComponent<Renderer>();
            MakeText("Label", b.label, new Vector3(0f, y, -0.014f), 0.04f, TextColor, ButtonWidth * 0.9f);
            y -= ButtonSpacing;
        }

        float bottom = y + ButtonSpacing - ButtonHeight * 0.5f - 0.05f;
        MakeBox("Background", new Vector3(0f, (top + bottom) * 0.5f, 0f), new Vector3(PanelWidth, top - bottom, 0.01f), PanelColor);
    }

    Transform MakeBox(string name, Vector3 localPosition, Vector3 scale, Color color)
    {
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        Destroy(box.GetComponent<Collider>()); // picking is done by ray / plane math, not physics
        box.transform.SetParent(panel, false);
        box.transform.localPosition = localPosition;
        box.transform.localScale = scale;

        var r = box.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        SetColor(r, color);
        return box.transform;
    }

    void MakeText(string name, string text, Vector3 localPosition, float height, Color color, float maxWidth)
    {
        var go = new GameObject(name);
        go.transform.SetParent(panel, false);
        go.transform.localPosition = localPosition;
        // the text of a TextMesh reads from its -Z side, so it faces back at the pilot with the panel
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.fontSize = 100;
        tm.characterSize = height / 10f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.text = text;

        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = font.material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.material.renderQueue = 4000;

        go.AddComponent<FitTextWidth>().maxWidth = maxWidth;
    }

    static void SetColor(Renderer r, Color color)
    {
        var m = r.material; // own instance
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
    }

    // Shrinks a text that is wider than allowed (after the mesh exists), then removes itself.
    class FitTextWidth : MonoBehaviour
    {
        public float maxWidth;

        void LateUpdate()
        {
            var r = GetComponent<MeshRenderer>();
            float width = r.localBounds.size.x * transform.lossyScale.x;
            if (width <= 0.0001f) return;

            if (width > maxWidth) transform.localScale *= maxWidth / width;
            Destroy(this);
        }
    }

    // ---------------------------------------------------------------- placement and input

    Transform Origin()
    {
        var cam = Camera.main;
        return cam != null ? cam.transform.parent : null; // the player: controller poses are relative to it
    }

    void PlacePanel()
    {
        var cam = Camera.main;
        if (panel == null || cam == null) return;

        var origin = Origin();
        Vector3 up = origin != null ? origin.up : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(cam.transform.forward, up);
        if (forward.sqrMagnitude < 0.01f) forward = origin != null ? origin.forward : Vector3.forward;
        forward.Normalize();

        panel.position = cam.transform.position + forward * PanelDistance;
        panel.rotation = Quaternion.LookRotation(forward, up);
    }

    void UpdateMenu()
    {
        if (panel == null) return;
        if (Time.unscaledTime < placeUntil) PlacePanel();

        int hit = -1;
        bool clicked = false;

        // desktop: mouse
        var cam = Camera.main;
        if (!XR.XRSettings.isDeviceActive && Mouse.current != null && cam != null)
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            int index = Pick(ray.origin, ray.direction, out _);
            if (index >= 0) hit = index;
            if (index >= 0 && Mouse.current.leftButton.wasPressedThisFrame) { Activate(index); return; }
        }

        // VR: both controllers
        if (XR.XRSettings.isDeviceActive)
        {
            var nodes = new[] { XR.XRNode.LeftHand, XR.XRNode.RightHand };
            if (beams == null) beams = new[] { MakeBeam(), MakeBeam() };

            for (int h = 0; h < 2; h++)
            {
                var device = XR.InputDevices.GetDeviceAtXRNode(nodes[h]);
                if (!device.isValid ||
                    !device.TryGetFeatureValue(XR.CommonUsages.devicePosition, out Vector3 p) ||
                    !device.TryGetFeatureValue(XR.CommonUsages.deviceRotation, out Quaternion q))
                {
                    beams[h].gameObject.SetActive(false);
                    handWasDown[h] = false;
                    continue;
                }

                var origin = Origin();
                Vector3 position = origin != null ? origin.TransformPoint(p) : p;
                Vector3 direction = (origin != null ? origin.rotation * q : q) * Vector3.forward;

                int index = Pick(position, direction, out float distance);
                if (index >= 0) hit = index;

                device.TryGetFeatureValue(XR.CommonUsages.triggerButton, out bool trigger);
                device.TryGetFeatureValue(XR.CommonUsages.primaryButton, out bool face);
                bool down = trigger || face;
                if (down && !handWasDown[h] && index >= 0) clicked = true;
                handWasDown[h] = down;

                SetBeam(beams[h], position, direction, index >= 0 ? distance : 2f);
                if (clicked) { Activate(index); return; }
            }
        }

        // keyboard / gamepad
        int step = 0;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.downArrowKey.wasPressedThisFrame || Keyboard.current.sKey.wasPressedThisFrame) step = 1;
            if (Keyboard.current.upArrowKey.wasPressedThisFrame || Keyboard.current.wKey.wasPressedThisFrame) step = -1;
        }
        if (Gamepad.current != null)
        {
            if (Gamepad.current.dpad.down.wasPressedThisFrame) step = 1;
            if (Gamepad.current.dpad.up.wasPressedThisFrame) step = -1;
        }
        if (step != 0)
        {
            keyboardIndex = (keyboardIndex + step + buttons.Count) % buttons.Count;
            hit = -1;
            hovered = -1;
        }

        bool confirm = (Keyboard.current != null &&
                        (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)) ||
                       (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
        if (confirm) { Activate(hovered >= 0 ? hovered : keyboardIndex); return; }

        if (hit >= 0) { hovered = hit; keyboardIndex = hit; }
        else if (step == 0) hovered = -1;

        for (int i = 0; i < buttons.Count; i++)
            SetColor(buttons[i].background, i == keyboardIndex ? ButtonHoverColor : ButtonColor);
    }

    void Activate(int index)
    {
        if (index < 0 || index >= buttons.Count) return;
        buttons[index].action();
    }

    // Ray against the panel's plane; returns the button under the hit point, or -1.
    int Pick(Vector3 origin, Vector3 direction, out float distance)
    {
        distance = 0f;
        Vector3 normal = panel.forward;
        float denominator = Vector3.Dot(direction, normal);
        if (Mathf.Abs(denominator) < 1e-5f) return -1;

        distance = Vector3.Dot(panel.position - origin, normal) / denominator;
        if (distance <= 0f) return -1;

        Vector3 local = panel.InverseTransformPoint(origin + direction * distance);
        for (int i = 0; i < buttons.Count; i++)
        {
            if (Mathf.Abs(local.x) <= ButtonWidth * 0.5f && Mathf.Abs(local.y - buttons[i].y) <= ButtonHeight * 0.5f)
                return i;
        }
        return -1;
    }

    Transform MakeBeam()
    {
        var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "MenuBeam";
        Destroy(beam.GetComponent<Collider>());
        beam.transform.SetParent(transform, false);
        var r = beam.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        SetColor(r, TitleColor);
        return beam.transform;
    }

    static void SetBeam(Transform beam, Vector3 origin, Vector3 direction, float length)
    {
        beam.gameObject.SetActive(true);
        beam.position = origin + direction * (length * 0.5f);
        beam.rotation = Quaternion.LookRotation(direction);
        beam.localScale = new Vector3(0.004f, 0.004f, length);
    }
}
