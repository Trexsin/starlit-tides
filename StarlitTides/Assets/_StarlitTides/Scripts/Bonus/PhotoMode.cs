// BONUS FEATURE 9 - Photo Mode
//
// Press P (or gamepad Select) while playing or paused:
// - time freezes and the HUD hides
// - fly a free camera around the boat:
//     A/D or left/right = orbit      W/S or up/down = tilt
//     Q/E or mouse wheel = zoom in/out
//     (or hold the right mouse button and drag; gamepad: right stick + triggers)
// - depth of field focuses on the boat: Z/X = more/less blur
// - 1-5 or Tab = colour filter (Natural, Moonlit, Golden, Noir, Dreamy)
// - Space or Enter = save a screenshot (PNG)
// - P or Esc = back to the game

using System.IO;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarlitTides
{
    public class PhotoMode : MonoBehaviour
    {
        public static bool IsActive { get; private set; }

        [Header("References")]
        public CinemachineCamera photoCamera;
        [Tooltip("Volume with depth of field and the colour filters (weight 0 outside photo mode).")]
        public Volume photoVolume;
        [Tooltip("Things to hide while taking photos (the HUD).")]
        public Behaviour[] hideWhileActive;

        [Header("Camera")]
        public float orbitSpeed = 70f;      // degrees per second
        public float zoomSpeed = 10f;       // metres per second
        public Vector2 distanceRange = new Vector2(3f, 60f);
        public Vector2 pitchRange = new Vector2(-5f, 80f);

        static readonly string[] FilterNames = { "Natural", "Moonlit", "Golden", "Noir", "Dreamy" };

        CinemachineBrain brain;
        Transform target;
        float yaw, pitch, distance;
        float aperture = 4f;
        int filter;
        string message;
        float messageTime = -10f;
        bool hideOverlayForCapture;

        DepthOfField dof;
        ColorAdjustments colour;
        Vignette vignette;
        FilmGrain grain;

        void Start()
        {
            if (photoVolume != null)
            {
                // ".profile" gives us a copy to change at runtime, so the saved asset is never modified.
                VolumeProfile profile = photoVolume.profile;
                profile.TryGet(out dof);
                profile.TryGet(out colour);
                profile.TryGet(out vignette);
                profile.TryGet(out grain);
                photoVolume.weight = 0f;
            }
        }

        void OnDisable()
        {
            if (IsActive) Exit();
        }

        void Update()
        {
            StarlitInput input = StarlitInput.Instance;
            GameManager game = GameManager.Instance;

            if (!IsActive)
            {
                bool canEnter = game != null && (game.State == GameState.Playing || game.State == GameState.Paused);
                if (canEnter && input != null && input.PhotoModePressed) Enter();
                return;
            }

            // Keep time frozen (hit-stop or slow motion could try to restart it).
            Time.timeScale = 0f;

            Keyboard kb = Keyboard.current;
            bool exit = (input != null && input.PhotoModePressed) || (kb != null && kb.escapeKey.wasPressedThisFrame);
            if (exit)
            {
                Exit();
                return;
            }

            MoveCamera();
            HandleSettings();
        }

        void Enter()
        {
            BoatController boat = BoatController.Instance;
            Camera cam = Camera.main;
            if (boat == null || cam == null || photoCamera == null) return;

            IsActive = true;
            target = boat.transform;
            brain = cam.GetComponent<CinemachineBrain>();
            if (brain != null) brain.IgnoreTimeScale = true;   // the camera still moves while time is frozen

            // Start the photo camera exactly where the game camera is.
            Vector3 offset = cam.transform.position - target.position;
            distance = Mathf.Clamp(offset.magnitude, distanceRange.x, distanceRange.y);
            yaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(offset.y / offset.magnitude, -1f, 1f)) * Mathf.Rad2Deg;
            PlaceCamera();
            photoCamera.Priority.Enabled = true;
            photoCamera.Priority.Value = 100;

            if (photoVolume != null) photoVolume.weight = 1f;
            foreach (Behaviour b in hideWhileActive) if (b != null) b.enabled = false;
            if (StarlitInput.Instance != null) StarlitInput.Instance.gameplayEnabled = false;
            ApplyFilter();
            Time.timeScale = 0f;
        }

        void Exit()
        {
            IsActive = false;
            photoCamera.Priority.Value = 0;
            if (brain != null) brain.IgnoreTimeScale = false;
            if (photoVolume != null) photoVolume.weight = 0f;
            foreach (Behaviour b in hideWhileActive) if (b != null) b.enabled = true;

            // Back to how the game was: running, or still paused.
            GameManager game = GameManager.Instance;
            bool paused = game != null && game.State == GameState.Paused;
            Time.timeScale = paused ? 0f : 1f;
            if (StarlitInput.Instance != null) StarlitInput.Instance.gameplayEnabled = game != null && game.IsPlaying;
        }

        void MoveCamera()
        {
            float dt = Time.unscaledDeltaTime;
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;

            float turn = 0f, tilt = 0f, zoom = 0f;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turn -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turn += 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) tilt += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) tilt -= 1f;
                if (kb.qKey.isPressed) zoom -= 1f;
                if (kb.eKey.isPressed) zoom += 1f;
            }
            if (pad != null)
            {
                turn += pad.rightStick.ReadValue().x;
                tilt += pad.rightStick.ReadValue().y;
                zoom += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            }

            yaw += turn * orbitSpeed * dt;
            pitch += tilt * orbitSpeed * 0.6f * dt;
            distance += zoom * zoomSpeed * dt;

            if (mouse != null)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * 0.2f;
                    pitch -= delta.y * 0.2f;
                }
                distance -= mouse.scroll.ReadValue().y * 0.01f;
            }

            pitch = Mathf.Clamp(pitch, pitchRange.x, pitchRange.y);
            distance = Mathf.Clamp(distance, distanceRange.x, distanceRange.y);
            PlaceCamera();

            // Keep the boat in focus.
            if (dof != null) dof.focusDistance.value = distance;
        }

        // Spherical coordinates (yaw, pitch, distance) around the boat -> camera position.
        void PlaceCamera()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 lookAt = target.position + Vector3.up * 0.8f;
            photoCamera.transform.position = lookAt + rotation * Vector3.forward * distance;
            photoCamera.transform.LookAt(lookAt);
        }

        void HandleSettings()
        {
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if (kb == null && pad == null) return;

            // Depth of field: Z = more blur (smaller aperture number), X = less.
            float dt = Time.unscaledDeltaTime;
            if (kb != null && kb.zKey.isPressed) aperture -= 4f * dt;
            if (kb != null && kb.xKey.isPressed) aperture += 4f * dt;
            aperture = Mathf.Clamp(aperture, 1f, 16f);
            if (dof != null) dof.aperture.value = aperture;

            // Filters.
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) SetFilter(0);
                if (kb.digit2Key.wasPressedThisFrame) SetFilter(1);
                if (kb.digit3Key.wasPressedThisFrame) SetFilter(2);
                if (kb.digit4Key.wasPressedThisFrame) SetFilter(3);
                if (kb.digit5Key.wasPressedThisFrame) SetFilter(4);
                if (kb.tabKey.wasPressedThisFrame) SetFilter((filter + 1) % FilterNames.Length);
            }
            if (pad != null && pad.rightShoulder.wasPressedThisFrame) SetFilter((filter + 1) % FilterNames.Length);

            // Screenshot.
            bool shoot = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                      || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            if (shoot) TakePhoto();
        }

        void SetFilter(int index)
        {
            filter = index;
            ApplyFilter();
            ShowMessage("Filter: " + FilterNames[filter]);
        }

        void ApplyFilter()
        {
            if (colour == null) return;

            // Start from "Natural", then change what each filter needs.
            colour.colorFilter.value = Color.white;
            colour.saturation.value = 0f;
            colour.contrast.value = 0f;
            colour.postExposure.value = 0f;
            if (vignette != null) vignette.intensity.value = 0.25f;
            if (grain != null) grain.intensity.value = 0f;

            switch (filter)
            {
                case 1: // Moonlit: cold and blue
                    colour.colorFilter.value = new Color(0.8f, 0.9f, 1.15f);
                    colour.saturation.value = -15f;
                    break;
                case 2: // Golden: warm lantern glow
                    colour.colorFilter.value = new Color(1.15f, 0.95f, 0.75f);
                    colour.saturation.value = 10f;
                    break;
                case 3: // Noir: black and white
                    colour.saturation.value = -100f;
                    colour.contrast.value = 30f;
                    if (grain != null) grain.intensity.value = 0.5f;
                    break;
                case 4: // Dreamy: bright, soft and pink
                    colour.colorFilter.value = new Color(1.05f, 0.9f, 1.1f);
                    colour.postExposure.value = 0.4f;
                    colour.saturation.value = 20f;
                    if (vignette != null) vignette.intensity.value = 0.45f;
                    break;
            }
        }

        void TakePhoto()
        {
            // In the editor save next to the project; in a build, in the player's data folder.
            string folder = Application.isEditor
                ? Path.Combine(Path.GetDirectoryName(Application.dataPath), "Photos")
                : Path.Combine(Application.persistentDataPath, "Photos");
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "StarlitTides_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");

            hideOverlayForCapture = true;   // don't put the help text in the photo
            ScreenCapture.CaptureScreenshot(file);
            ShowMessage("Saved " + file);
        }

        void ShowMessage(string text)
        {
            message = text;
            messageTime = Time.unscaledTime;
        }

        void OnGUI()
        {
            if (!IsActive) return;
            if (hideOverlayForCapture)
            {
                // Skip one frame of overlay so the screenshot is clean.
                if (Event.current.type == EventType.Repaint) hideOverlayForCapture = false;
                return;
            }

            GUIStyle style = new GUIStyle(GUI.skin.label);
            style.fontSize = 16;
            style.normal.textColor = new Color(0.9f, 0.95f, 1f, 0.9f);

            GUI.Label(new Rect(20, Screen.height - 90, 1200, 24),
                "PHOTO MODE   A/D W/S orbit   Q/E zoom   Z/X blur (f/" + aperture.ToString("0.0") + ")   1-5 filter (" + FilterNames[filter] + ")", style);
            GUI.Label(new Rect(20, Screen.height - 64, 1200, 24), "Space = take photo     P / Esc = back to the game", style);

            if (Time.unscaledTime - messageTime < 2.5f)
                GUI.Label(new Rect(20, 20, 1400, 24), message, style);
        }
    }
}
