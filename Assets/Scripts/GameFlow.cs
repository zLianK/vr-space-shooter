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

    const float OpenSeconds = 0.18f;    // the panel "powers on" by unfolding vertically

    // holographic ship terminal: translucent deep blue, cyan lines and text
    // opaque: the cockpit HUD behind it is bright HDR emission, which shows through even 5% transparency
    static readonly Color PanelColor = new Color(0.012f, 0.03f, 0.065f, 1f);
    static readonly Color PanelEdgeColor = new Color(0.35f, 0.9f, 1f, 1f);
    static readonly Color ButtonColor = new Color(0.1f, 0.5f, 0.65f, 0.16f);
    static readonly Color ButtonEdgeColor = new Color(0.35f, 0.9f, 1f, 0.6f);
    static readonly Color ButtonHoverColor = new Color(0.15f, 0.75f, 0.9f, 0.55f);
    static readonly Color ButtonHoverEdgeColor = new Color(0.7f, 1f, 1f, 1f);
    static readonly Color TextColor = new Color(0.85f, 0.95f, 1f);
    static readonly Color TextHoverColor = Color.white;
    static readonly Color TitleColor = new Color(0.4f, 0.95f, 1f);
    static readonly Color DimColor = new Color(0.4f, 0.65f, 0.78f);

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
        public TextMesh text;
    }

    Transform panel;
    readonly List<Button> buttons = new List<Button>();
    readonly bool[] handWasDown = new bool[2];
    Transform[] beams;
    int keyboardIndex;
    int hovered = -1;
    float openedAt;

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
        PlayerController.Recentered += PlacePanel; // VR: the head may be recentered after the menu opened

        ShowMenu(State.Menu);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        PlayerController.Recentered -= PlacePanel;
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
        openedAt = Time.unscaledTime;
        PlacePanel();
        AnimatePanel();
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

        // eyebrow: small status line above the title, like a ship terminal header
        string eyebrow, title, subtitle = null, score = null;
        switch (state)
        {
            case State.Paused:
                eyebrow = "SISTEMAS EM ESPERA";
                title = "PAUSADO";
                buttons.Add(new Button { label = "CONTINUAR", action = Resume });
                buttons.Add(new Button { label = "REINICIAR", action = Restart });
                buttons.Add(new Button { label = "FECHAR", action = Quit });
                break;
            case State.Finished:
                eyebrow = "FIM DA MISSÃO";
                title = "TEMPO ESGOTADO";
                score = SpaceDebris.DestroyedCount.ToString();
                buttons.Add(new Button { label = "JOGAR NOVAMENTE", action = Restart });
                buttons.Add(new Button { label = "FECHAR", action = Quit });
                break;
            default:
                eyebrow = "MISSÃO  //  CINTURÃO DE ASTEROIDES";
                title = "VR SPACE SHOOTER";
                subtitle = "Destrua o máximo de asteroides em " + Mathf.RoundToInt(MatchSeconds) + " segundos";
                buttons.Add(new Button { label = "INICIAR", action = StartMatch });
                buttons.Add(new Button { label = "FECHAR", action = Quit });
                break;
        }

        // layout from the top: eyebrow, title, divider, subtitle or score, buttons, footer
        const float textZ = -0.012f;
        float textWidth = PanelWidth * 0.86f;
        float y = 0.2f;
        float top = y + 0.06f;
        MakeText("Eyebrow", eyebrow, new Vector3(0f, y, textZ), 0.022f, DimColor, textWidth);
        y -= 0.06f;
        MakeText("Title", title, new Vector3(0f, y, textZ), 0.062f, TitleColor, textWidth);
        y -= 0.05f;

        // divider: a faint line with a bright segment in the middle
        MakeRect("Divider", new Vector2(0f, y), new Vector2(PanelWidth * 0.78f, 0.0015f), PanelEdgeColor * new Color(1f, 1f, 1f, 0.35f), DecorQueue);
        MakeRect("DividerCenter", new Vector2(0f, y), new Vector2(0.12f, 0.003f), PanelEdgeColor, DecorQueue);
        y -= 0.055f;

        if (score != null)
        {
            y -= 0.01f;
            MakeText("Score", score, new Vector3(0f, y, textZ), 0.12f, TextHoverColor, textWidth);
            y -= 0.07f;
            MakeText("ScoreLabel", "ASTEROIDES DESTRUÍDOS", new Vector3(0f, y, textZ), 0.024f, DimColor, textWidth);
            y -= 0.075f;
        }
        else if (subtitle != null)
        {
            MakeText("Subtitle", subtitle, new Vector3(0f, y, textZ), 0.03f, TextColor, textWidth);
            y -= 0.075f;
        }
        else
        {
            y -= 0.01f;
        }

        foreach (var b in buttons)
        {
            b.y = y;
            b.background = MakePanel("Button", new Vector2(0f, y), new Vector2(ButtonWidth, ButtonHeight),
                                     ButtonColor, ButtonEdgeColor, ButtonQueue, chamfer: 0.014f, corner: 0.018f, glow: 0.012f, scanlines: 0f);
            b.text = MakeText("Label", b.label, new Vector3(0f, y, textZ), 0.036f, TextColor, ButtonWidth * 0.86f);
            y -= ButtonSpacing;
        }

        y += ButtonSpacing - ButtonHeight * 0.5f - 0.045f;
        MakeText("Footer", "SISTEMAS ONLINE   ·   SETOR 7G", new Vector3(0f, y, textZ), 0.017f, DimColor, textWidth);
        float bottom = y - 0.035f;

        MakePanel("Background", new Vector2(0f, (top + bottom) * 0.5f), new Vector2(PanelWidth, top - bottom),
                  PanelColor, PanelEdgeColor, PanelQueue, chamfer: 0.035f, corner: 0.05f, glow: 0.03f, scanlines: 1f);
    }

    // A holographic panel (see the "Game/Menu Overlay" shader): fill, outline, corner brackets, cut corners.
    Renderer MakePanel(string name, Vector2 position, Vector2 size, Color fill, Color edge, int queue,
                       float chamfer, float corner, float glow, float scanlines)
    {
        var r = MakeRect(name, position, size, fill, queue);
        var m = r.material;
        m.SetFloat("_Shape", 1f);
        m.SetVector("_Size", size);
        m.SetColor("_EdgeColor", edge);
        m.SetFloat("_Chamfer", chamfer);
        m.SetFloat("_Corner", corner);
        m.SetFloat("_Glow", glow);
        m.SetFloat("_Scanlines", scanlines);
        return r;
    }

    // A flat colored rectangle on the panel, facing the pilot.
    Renderer MakeRect(string name, Vector2 position, Vector2 size, Color color, int queue)
    {
        // a quad is seen from its -Z side, the side facing the pilot (the panel's +Z points away from them)
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        Destroy(quad.GetComponent<Collider>()); // picking is done by ray / plane math, not physics
        quad.transform.SetParent(panel, false);
        quad.transform.localPosition = position;
        quad.transform.localScale = new Vector3(size.x, size.y, 1f);

        var r = quad.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        UseOverlayMaterial(r, queue);
        SetColor(r, color);
        return r;
    }

    TextMesh MakeText(string name, string text, Vector3 localPosition, float height, Color color, float maxWidth)
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
        r.material.renderQueue = TextQueue;
        // GUI/Text Shader reads its depth test from this property: Always = on top of the cockpit, like the panel
        r.material.SetFloat("unity_GUIZTestMode", (float)UnityEngine.Rendering.CompareFunction.Always);

        go.AddComponent<FitTextWidth>().maxWidth = maxWidth;
        return tm;
    }

    // Draw order on top of the cockpit: beams, then the panel, then its buttons and lines, then the text.
    const int BeamQueue = 3999, PanelQueue = 4000, ButtonQueue = 4001, DecorQueue = 4001, TextQueue = 4002;

    // A material asset (in Resources), so its shader is part of the build: the default material of
    // CreatePrimitive is not, and renders pink (missing shader) in the player. Its shader ignores depth,
    // so cockpit parts between the pilot and the menu (the HUD) never cover it.
    static Material overlayMaterial;

    static void UseOverlayMaterial(Renderer r, int queue)
    {
        if (overlayMaterial == null) overlayMaterial = Resources.Load<Material>("MenuOverlay");
        if (overlayMaterial != null) r.sharedMaterial = overlayMaterial;
        r.material.renderQueue = queue; // own instance, so each box can have its own color
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
        return cam != null ? cam.transform.parent : null; // the tracking space: controller poses are relative to it
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

    // Unfolds the panel from a thin line, like a hologram powering on (runs on unscaled time: the game is paused).
    void AnimatePanel()
    {
        float t = Mathf.Clamp01((Time.unscaledTime - openedAt) / OpenSeconds);
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
        panel.localScale = new Vector3(Mathf.Lerp(0.6f, 1f, eased), Mathf.Lerp(0.02f, 1f, eased), 1f);
    }

    void UpdateMenu()
    {
        if (panel == null) return;
        if (Time.unscaledTime < placeUntil) PlacePanel();
        AnimatePanel();
        Shader.SetGlobalFloat("_MenuTime", Time.unscaledTime); // drives the scanline sweep

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
        {
            bool lit = i == keyboardIndex;
            var m = buttons[i].background.material;
            m.SetColor("_BaseColor", lit ? ButtonHoverColor : ButtonColor);
            m.SetColor("_EdgeColor", lit ? ButtonHoverEdgeColor : ButtonEdgeColor);
            buttons[i].text.color = lit ? TextHoverColor : TextColor;
        }
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
        UseOverlayMaterial(r, BeamQueue);
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
