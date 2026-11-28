// SettingsView: connects the Settings screen's sliders, toggles, dropdowns and
// buttons to the SettingsManager / DisplayManager, and shows the current values.
//
// Phase 9: two columns. GENERAL (volumes, fullscreen, FPS counter, intro, battery
// saver, 30 fps in menus, automatic resolution drop) and GRAPHICS (quality preset,
// display resolution, 3D resolution, anti-aliasing, shadows, reflections, colour
// depth, frame rate, auto-detect). Every choice is the player's.
//
// Changes wait for the APPLY button: the screen edits a pending copy of the
// settings, APPLY hands it to the managers, and BACK (or leaving the screen)
// drops whatever wasn't applied. The volume sliders are the exception: they
// change straight away, so you can hear the level while you drag.

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarlitTides
{
    public class SettingsView : MonoBehaviour
    {
        public Slider masterSlider;
        public Slider musicSlider;
        public Slider sfxSlider;
        public TextMeshProUGUI qualityText;
        public Button qualityLeft;
        public Button qualityRight;
        [Tooltip("Display resolution: the size of the whole picture (DisplayManager.DisplayOptions).")]
        public TMP_Dropdown resolutionDropdown;
        [Tooltip("3D resolution: 100% .. 50% of the display resolution.")]
        public TMP_Dropdown renderDropdown;
        public TMP_Dropdown antiAliasingDropdown;
        public TMP_Dropdown shadowsDropdown;
        public TMP_Dropdown reflectionsDropdown;
        public TMP_Dropdown colorDepthDropdown;
        public TMP_Dropdown frameRateDropdown;
        public TextMeshProUGUI resolutionDetail;
        public Button autoDetectButton;
        public Toggle fullscreenToggle;
        public Toggle fpsToggle;
        public Toggle introToggle;
        public Toggle batterySaverToggle;
        public Toggle menuFpsToggle;
        public Toggle adaptiveToggle;
        [Tooltip("Applies the pending changes.")]
        public Button applyButton;
        [Tooltip("'Unapplied changes' / 'Settings applied'.")]
        public TextMeshProUGUI applyStatus;

        /// <summary>Everything the screen can change (except the volumes, which are live).</summary>
        struct Values
        {
            public int quality, antiAliasing, shadows, reflections, colorDepth, frameRate, display, render;
            public bool fullscreen, showFps, intro, batterySaver, menuFps, adaptive;
        }

        Values pending;
        bool refreshing;
        float appliedMessageUntil;

        /// <summary>True while the screen shows changes that haven't been applied.</summary>
        public bool Dirty { get { return !pending.Equals(Saved()); } }

        void Awake()
        {
            masterSlider.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetMasterVolume(v); });
            musicSlider.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetMusicVolume(v); });
            sfxSlider.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetSfxVolume(v); });
            qualityLeft.onClick.AddListener(() => ChangeQuality(-1));
            qualityRight.onClick.AddListener(() => ChangeQuality(1));
            Bind(fullscreenToggle, v => pending.fullscreen = v);
            Bind(fpsToggle, v => pending.showFps = v);
            Bind(introToggle, v => pending.intro = v);
            Bind(batterySaverToggle, v => pending.batterySaver = v);
            Bind(menuFpsToggle, v => pending.menuFps = v);
            Bind(adaptiveToggle, v => pending.adaptive = v);

            Bind(resolutionDropdown, null, i => pending.display = i);
            var percents = new List<string>();
            for (int i = 0; i < DisplayManager.RenderFractions.Length; i++) percents.Add(DisplayManager.RenderOptionLabel(i));
            Bind(renderDropdown, percents, i => pending.render = i);
            Bind(antiAliasingDropdown, new List<string>(SettingsManager.AntiAliasingNames), i => pending.antiAliasing = i);
            Bind(shadowsDropdown, new List<string>(SettingsManager.ShadowNames), i => pending.shadows = i);
            Bind(reflectionsDropdown, new List<string>(SettingsManager.ReflectionNames), i => pending.reflections = i);
            Bind(colorDepthDropdown, new List<string>(SettingsManager.ColorDepthNames), i => pending.colorDepth = i);
            Bind(frameRateDropdown, new List<string>(SettingsManager.FrameRateNames), i => pending.frameRate = i);
            if (applyButton != null) applyButton.onClick.AddListener(Apply);
            if (autoDetectButton != null) autoDetectButton.onClick.AddListener(() =>
            {
                // Auto-detect measures and saves real settings: drop the pending edits first.
                if (DisplayManager.Instance == null) return;
                pending = Saved();
                DisplayManager.Instance.AutoDetect();
                Refresh();
            });
        }

        void Bind(Toggle toggle, System.Action<bool> set)
        {
            if (toggle != null) toggle.onValueChanged.AddListener(v => { if (!refreshing) { set(v); Refresh(); } });
        }

        void Bind(TMP_Dropdown dropdown, List<string> options, System.Action<int> set)
        {
            if (dropdown == null) return;
            if (options != null) { dropdown.ClearOptions(); dropdown.AddOptions(options); }
            dropdown.onValueChanged.AddListener(i => { if (!refreshing) { set(i); Refresh(); } });
        }

        // Live updates: the auto-detect status, a window resize, the fullscreen switch.
        void OnEnable()
        {
            if (DisplayManager.Instance != null) DisplayManager.Instance.Changed += OnDisplayChanged;
        }

        void OnDisable()
        {
            if (DisplayManager.Instance != null) DisplayManager.Instance.Changed -= OnDisplayChanged;
        }

        void Update()
        {
            // (Fades the "Settings applied" note out.)
            if (appliedMessageUntil > 0f && Time.unscaledTime > appliedMessageUntil) { appliedMessageUntil = 0f; Refresh(); }
        }

        void OnDisplayChanged()
        {
            // Something changed the real settings (auto-detect, the automatic resolution
            // drop): follow them, unless the player is in the middle of editing.
            DisplayManager display = DisplayManager.Instance;
            if (!Dirty || (display != null && display.Detecting)) pending = Saved();
            Refresh();
        }

        /// <summary>Called when the screen opens: shows the saved settings, nothing pending.</summary>
        public void Open()
        {
            pending = Saved();
            appliedMessageUntil = 0f;
            Refresh();
        }

        // The quality preset brings its own shadows and reflections (changeable afterwards).
        void ChangeQuality(int direction)
        {
            int count = QualitySettings.names.Length;
            pending.quality = (pending.quality + direction + count) % count;
            pending.shadows = SettingsManager.PresetShadows(pending.quality);
            pending.reflections = SettingsManager.PresetReflections(pending.quality);
            Refresh();
        }

        static Values Saved()
        {
            Values v = new Values();
            SettingsManager s = SettingsManager.Instance;
            if (s != null)
            {
                v.quality = s.QualityLevel; v.antiAliasing = s.AntiAliasing; v.shadows = s.Shadows; v.reflections = s.Reflections;
                v.colorDepth = s.ColorDepth; v.frameRate = s.FrameRate; v.fullscreen = s.Fullscreen; v.showFps = s.ShowFps;
                v.intro = s.PlayIntro; v.batterySaver = s.BatterySaver; v.menuFps = s.MenuLowFps; v.adaptive = s.AdaptiveResolution;
            }
            DisplayManager d = DisplayManager.Instance;
            if (d != null) { v.display = d.DisplayIndex; v.render = d.RenderIndex; }
            return v;
        }

        /// <summary>Hands the pending changes to the managers (only what changed).</summary>
        public void Apply()
        {
            SettingsManager s = SettingsManager.Instance;
            DisplayManager d = DisplayManager.Instance;
            if (s == null) return;
            Values want = pending;
            Values now = Saved();
            // The preset first: it resets shadows and reflections, which are set after it.
            if (want.quality != now.quality) s.SetQuality(want.quality);
            if (want.shadows != s.Shadows) s.SetShadows(want.shadows);
            if (want.reflections != s.Reflections) s.SetReflections(want.reflections);
            if (want.antiAliasing != now.antiAliasing) s.SetAntiAliasing(want.antiAliasing);
            if (want.colorDepth != now.colorDepth) s.SetColorDepth(want.colorDepth);
            if (want.frameRate != now.frameRate) s.SetFrameRate(want.frameRate);
            if (want.menuFps != now.menuFps) s.SetMenuLowFps(want.menuFps);
            if (want.batterySaver != now.batterySaver) s.SetBatterySaver(want.batterySaver);
            if (want.adaptive != now.adaptive) s.SetAdaptiveResolution(want.adaptive);
            if (want.showFps != now.showFps) s.SetShowFps(want.showFps);
            if (want.intro != now.intro) s.SetPlayIntro(want.intro);
            if (d != null && want.display != now.display) d.SetDisplayResolution(want.display);
            if (d != null && want.render != now.render) d.SetRenderResolution(want.render);
            if (want.fullscreen != now.fullscreen) s.SetFullscreen(want.fullscreen);   // last: it changes the window
            PlayerPrefs.Save();
            pending = Saved();
            appliedMessageUntil = Time.unscaledTime + 2.5f;
            Refresh();
        }

        static void Show(TMP_Dropdown dropdown, int value, bool interactable = true)
        {
            if (dropdown == null) return;
            dropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, dropdown.options.Count - 1));
            dropdown.RefreshShownValue();
            dropdown.interactable = interactable;
        }

        static void Show(Toggle toggle, bool value)
        {
            if (toggle != null) toggle.SetIsOnWithoutNotify(value);
        }

        // Shows the pending values (without triggering the change events).
        public void Refresh()
        {
            SettingsManager settings = SettingsManager.Instance;
            if (settings == null) return;

            refreshing = true;
            masterSlider.value = settings.MasterVolume;
            musicSlider.value = settings.MusicVolume;
            sfxSlider.value = settings.SfxVolume;
            qualityText.text = QualitySettings.names[Mathf.Clamp(pending.quality, 0, QualitySettings.names.Length - 1)].ToUpper();
            Show(fullscreenToggle, pending.fullscreen);
            Show(fpsToggle, pending.showFps);
            Show(introToggle, pending.intro);
            Show(batterySaverToggle, pending.batterySaver);
            Show(menuFpsToggle, pending.menuFps);
            Show(adaptiveToggle, pending.adaptive);

            DisplayManager display = DisplayManager.Instance;
            bool free = display == null || !display.Detecting;
            Show(antiAliasingDropdown, pending.antiAliasing, free);
            Show(shadowsDropdown, pending.shadows, free);
            Show(reflectionsDropdown, pending.reflections, free);
            Show(colorDepthDropdown, pending.colorDepth, free);
            Show(frameRateDropdown, pending.frameRate, free);
            qualityLeft.interactable = qualityRight.interactable = free;
            if (display != null && resolutionDropdown != null)
            {
                // Rebuild the list only when it changed (a different display).
                bool same = resolutionDropdown.options.Count == display.DisplayOptions.Count;
                for (int i = 0; same && i < display.DisplayOptions.Count; i++)
                    same = resolutionDropdown.options[i].text == display.DisplayOptionLabel(i);
                if (!same)
                {
                    resolutionDropdown.ClearOptions();
                    var labels = new List<string>();
                    for (int i = 0; i < display.DisplayOptions.Count; i++) labels.Add(display.DisplayOptionLabel(i));
                    resolutionDropdown.AddOptions(labels);
                }
                Show(resolutionDropdown, pending.display, free);
                Show(renderDropdown, pending.render, free);
                if (resolutionDetail != null) resolutionDetail.text = display.DetailLabel();
                if (autoDetectButton != null) autoDetectButton.interactable = free;
            }

            bool dirty = Dirty;
            if (applyButton != null)
            {
                applyButton.interactable = dirty && free;
                // Fade the whole button (text too) while there is nothing to apply.
                CanvasGroup fade = applyButton.GetComponent<CanvasGroup>();
                if (fade == null) fade = applyButton.gameObject.AddComponent<CanvasGroup>();
                fade.alpha = applyButton.interactable ? 1f : 0.35f;
            }
            if (applyStatus != null)
            {
                if (dirty) { applyStatus.text = "You have changes: press APPLY to use them (BACK discards them)"; applyStatus.color = new Color(1f, 0.82f, 0.5f); }
                else if (appliedMessageUntil > 0f) { applyStatus.text = "Settings applied"; applyStatus.color = new Color(0.55f, 0.92f, 1f); }
                else applyStatus.text = "";
            }
            refreshing = false;
        }
    }
}
