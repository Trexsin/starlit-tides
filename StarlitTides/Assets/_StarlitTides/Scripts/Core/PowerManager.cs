// PowerManager: keeps the laptop cool and the battery going (Phase 9).
//
// Measured on a MacBook Air: during play the GPU was ~88% busy, even in menus,
// which heats the machine and drains the battery. So (each one a player setting):
//   - FRAME RATE: 30 fps, 60 fps, match the display (vSync, the default) or unlimited.
//   - 30 FPS IN MENUS (default on): menus, the pause screen and game over run at
//     30 fps: nothing there needs 60, and it halves the work.
//   - BATTERY SAVER (off unless the player turns it on): while the laptop runs on
//     battery (Mac or Windows) the game is capped at 30 fps, the god rays are
//     switched off and the far-off scenery is sparser. It never lowers the
//     resolution (that made the picture blurry behind the player's back: the
//     Settings screen still showed their own choice). Back to normal when plugged in.
// It never touches anything while the display auto-detect is measuring.

using UnityEngine;

namespace StarlitTides
{
    [DefaultExecutionOrder(-900)]
    public class PowerManager : MonoBehaviour
    {
        public static PowerManager Instance { get; private set; }

        /// <summary>True while the battery saver is active (on battery, and the player has it switched on).</summary>
        public static bool OnBattery { get; private set; }

        /// <summary>Re-applies the frame rate and battery saver after a settings change.</summary>
        public static void Reapply()
        {
            if (Instance == null) return;
            Instance.applied = false;
            Instance.batteryCheck = 0f;
        }


        bool menuMode;
        bool applied;
        float batteryCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null) return;
            var go = new GameObject("Power Manager");
            DontDestroyOnLoad(go);
            go.AddComponent<PowerManager>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            OnBattery = ReadBattery();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        static bool ReadBattery()
        {
            // Desktops report Unknown / NotCharging with no battery: only Discharging counts.
            bool enabled = SettingsManager.Instance == null || SettingsManager.Instance.BatterySaver;
            return enabled && SystemInfo.batteryStatus == BatteryStatus.Discharging;
        }

        void Update()
        {
            // ---- battery saver ----
            batteryCheck -= Time.unscaledDeltaTime;
            if (batteryCheck <= 0f)
            {
                batteryCheck = 5f;
                bool battery = ReadBattery();
                if (battery != OnBattery)
                {
                    OnBattery = battery;
                    Debug.Log("[Starlit Tides] Battery saver " + (battery ? "on (running on battery)" : "off (plugged in)"));
                    if (SettingsManager.Instance != null) SettingsManager.Instance.ReapplyQuality();   // god rays, render scale
                    if (DisplayManager.Instance != null) DisplayManager.Instance.ApplyRenderScale();    // (refreshes the Settings detail line)
                    applied = false;                                                                      // frame rate
                }
            }

            // ---- frame rate (30 fps outside of play if chosen) ----
            DisplayManager display = DisplayManager.Instance;
            if (display != null && display.Detecting) { applied = false; return; }   // auto-detect owns the frame rate
            GameManager game = GameManager.Instance;
            SettingsManager settings = SettingsManager.Instance;
            bool menu = game != null && game.State != GameState.Playing && game.State != GameState.Intro;
            if (applied && menu == menuMode) return;
            menuMode = menu;
            applied = true;
            int choice = settings != null ? settings.FrameRate : SettingsManager.FrameRateMatch;
            if (menu && (settings == null || settings.MenuLowFps)) choice = SettingsManager.FrameRate30;
            if (OnBattery) choice = SettingsManager.FrameRate30;   // the battery saver's main saving
            ApplyFrameRate(choice);
        }

        /// <summary>
        /// 30 / 60: vSync when the display runs at exactly that rate (perfectly even
        /// frames, and FramePacer can lock the clock), otherwise a frame cap.
        /// Match display: vSync. Unlimited: as fast as the machine can (hot!).
        /// </summary>
        static void ApplyFrameRate(int choice)
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            if (hz < 1.0) hz = 60.0;
            int fps = choice == SettingsManager.FrameRate30 ? 30 : choice == SettingsManager.FrameRate60 ? 60 : 0;
            if (choice == SettingsManager.FrameRateUnlimited)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            else if (fps == 0 || System.Math.Abs(hz - fps) < 1.5)
            {
                QualitySettings.vSyncCount = 1;
                Application.targetFrameRate = -1;
            }
            else
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = fps;
            }
        }
    }
}
