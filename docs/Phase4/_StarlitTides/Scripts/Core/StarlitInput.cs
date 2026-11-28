// StarlitInput: reads the keyboard and gamepad using the new Input System.
//
// The input actions are made in code, so there is no .inputactions file to set up.
//   Move    = WASD, arrow keys, left stick, d-pad
//   Dash    = Space, gamepad A / Cross
//   Pause   = Esc, gamepad Start
//   Restart = R, gamepad Y / Triangle
//   Photo   = P, gamepad Select (used later for Photo Mode)
//   Skip    = Space, Enter or Esc, gamepad A or Start (skips the intro)
// Other scripts just read StarlitInput.Instance.Move, .DashPressed and so on.

using UnityEngine;
using UnityEngine.InputSystem;

namespace StarlitTides
{
    // Run before other scripts so they all see this frame's input.
    [DefaultExecutionOrder(-100)]
    public class StarlitInput : MonoBehaviour
    {
        public static StarlitInput Instance { get; private set; }

        // Turned off during menus, cutscenes and game over.
        public bool gameplayEnabled = true;

        InputAction moveAction;
        InputAction dashAction;
        InputAction pauseAction;
        InputAction restartAction;
        InputAction photoAction;
        InputAction skipAction;

        // Steering direction. x = left/right, y = forward/back. Length is at most 1.
        public Vector2 Move
        {
            get
            {
                if (!gameplayEnabled) return Vector2.zero;
                return Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
            }
        }

        public bool DashPressed { get { return gameplayEnabled && dashAction.WasPressedThisFrame(); } }
        public bool PausePressed { get { return pauseAction.WasPressedThisFrame(); } }
        public bool RestartPressed { get { return restartAction.WasPressedThisFrame(); } }
        public bool PhotoModePressed { get { return photoAction.WasPressedThisFrame(); } }
        public bool SkipPressed { get { return skipAction.WasPressedThisFrame(); } }

        void Awake()
        {
            // Only one input reader in the scene.
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            moveAction = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            moveAction.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
            moveAction.AddBinding("<Gamepad>/leftStick");
            moveAction.AddBinding("<Gamepad>/dpad");

            dashAction = new InputAction("Dash", InputActionType.Button, "<Keyboard>/space");
            dashAction.AddBinding("<Gamepad>/buttonSouth");

            pauseAction = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            pauseAction.AddBinding("<Gamepad>/start");

            restartAction = new InputAction("Restart", InputActionType.Button, "<Keyboard>/r");
            restartAction.AddBinding("<Gamepad>/buttonNorth");

            photoAction = new InputAction("PhotoMode", InputActionType.Button, "<Keyboard>/p");
            photoAction.AddBinding("<Gamepad>/select");

            skipAction = new InputAction("Skip", InputActionType.Button, "<Keyboard>/space");
            skipAction.AddBinding("<Keyboard>/enter");
            skipAction.AddBinding("<Keyboard>/escape");
            skipAction.AddBinding("<Gamepad>/buttonSouth");
            skipAction.AddBinding("<Gamepad>/start");
        }

        void OnEnable()
        {
            if (moveAction == null) return;   // Awake destroyed a duplicate
            moveAction.Enable();
            dashAction.Enable();
            pauseAction.Enable();
            restartAction.Enable();
            photoAction.Enable();
            skipAction.Enable();
        }

        void OnDisable()
        {
            if (moveAction == null) return;
            moveAction.Disable();
            dashAction.Disable();
            pauseAction.Disable();
            restartAction.Disable();
            photoAction.Disable();
            skipAction.Disable();
        }

        void OnDestroy()
        {
            if (moveAction != null)
            {
                moveAction.Dispose();
                dashAction.Dispose();
                pauseAction.Dispose();
                restartAction.Dispose();
                photoAction.Dispose();
                skipAction.Dispose();
            }
            if (Instance == this) Instance = null;
        }
    }
}
