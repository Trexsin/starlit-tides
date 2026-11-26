// =============================================================================
//  StarlitInput - all player input, using the new Input System
// -----------------------------------------------------------------------------
//  The actions are created in code (no .inputactions asset to set up), with
//  keyboard AND gamepad bindings:
//      Move     WASD / Arrow keys / left stick / d-pad
//      Dash     Space / gamepad South button (A on Xbox, Cross on PlayStation)
//      Pause    Esc / gamepad Start
//      Restart  R / gamepad North button (only used on the game-over screen)
//  Other scripts read StarlitInput.Instance.Move, .DashPressed, etc.
// =============================================================================
using UnityEngine;
using UnityEngine.InputSystem;

namespace StarlitTides
{
    [DefaultExecutionOrder(-100)]   // read input before anything else uses it
    public class StarlitInput : MonoBehaviour
    {
        public static StarlitInput Instance { get; private set; }

        [Tooltip("When false, Move returns zero and Dash is ignored (menus, cinematics, game over).")]
        public bool gameplayEnabled = true;

        private InputAction _move, _dash, _pause, _restart, _photo;

        /// <summary>Steering direction, each axis -1..1 (x = left/right, y = forward/back).</summary>
        public Vector2 Move => gameplayEnabled ? Vector2.ClampMagnitude(_move.ReadValue<Vector2>(), 1f) : Vector2.zero;
        public bool DashPressed => gameplayEnabled && _dash.WasPressedThisFrame();
        public bool PausePressed => _pause.WasPressedThisFrame();
        public bool RestartPressed => _restart.WasPressedThisFrame();
        public bool PhotoModePressed => _photo.WasPressedThisFrame();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;

            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");
            _move.AddBinding("<Gamepad>/dpad");

            _dash = new InputAction("Dash", InputActionType.Button, "<Keyboard>/space");
            _dash.AddBinding("<Gamepad>/buttonSouth");

            _pause = new InputAction("Pause", InputActionType.Button, "<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/start");

            _restart = new InputAction("Restart", InputActionType.Button, "<Keyboard>/r");
            _restart.AddBinding("<Gamepad>/buttonNorth");

            _photo = new InputAction("PhotoMode", InputActionType.Button, "<Keyboard>/p");
            _photo.AddBinding("<Gamepad>/select");
        }

        private void OnEnable()
        {
            _move?.Enable(); _dash?.Enable(); _pause?.Enable(); _restart?.Enable(); _photo?.Enable();
        }

        private void OnDisable()
        {
            _move?.Disable(); _dash?.Disable(); _pause?.Disable(); _restart?.Disable(); _photo?.Disable();
        }

        private void OnDestroy()
        {
            _move?.Dispose(); _dash?.Dispose(); _pause?.Dispose(); _restart?.Dispose(); _photo?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
