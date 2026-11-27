// SettingsView: connects the Settings screen's sliders, toggles and buttons
// to the SettingsManager, and shows the current values.

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
        [Tooltip("Lists real resolutions (DisplayManager.Options).")]
        public TMP_Dropdown resolutionDropdown;
        public TextMeshProUGUI resolutionDetail;
        public Button autoDetectButton;
        public Toggle fullscreenToggle;
        public Toggle fpsToggle;
        public Toggle introToggle;

        bool refreshing;

        void Awake()
        {
            masterSlider.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetMasterVolume(v); });
            musicSlider.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetMusicVolume(v); });
            sfxSlider.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetSfxVolume(v); });
            qualityLeft.onClick.AddListener(() => ChangeQuality(-1));
            qualityRight.onClick.AddListener(() => ChangeQuality(1));
            fullscreenToggle.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetFullscreen(v); });
            fpsToggle.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetShowFps(v); });
            introToggle.onValueChanged.AddListener(v => { if (!refreshing) SettingsManager.Instance.SetPlayIntro(v); });
            if (resolutionDropdown != null)
                resolutionDropdown.onValueChanged.AddListener(i => { if (!refreshing && DisplayManager.Instance != null) DisplayManager.Instance.SetResolution(i); });
            if (autoDetectButton != null) autoDetectButton.onClick.AddListener(() => { if (DisplayManager.Instance != null) DisplayManager.Instance.AutoDetect(); });
        }

        // Live updates: the auto-detect status, a window resize, the fullscreen switch.
        void OnEnable()
        {
            if (DisplayManager.Instance != null) DisplayManager.Instance.Changed += Refresh;
        }

        void OnDisable()
        {
            if (DisplayManager.Instance != null) DisplayManager.Instance.Changed -= Refresh;
        }

        void ChangeQuality(int direction)
        {
            SettingsManager.Instance.StepQuality(direction);
            Refresh();
        }

        // Shows the saved values (without triggering the change events).
        public void Refresh()
        {
            SettingsManager settings = SettingsManager.Instance;
            if (settings == null) return;

            refreshing = true;
            masterSlider.value = settings.MasterVolume;
            musicSlider.value = settings.MusicVolume;
            sfxSlider.value = settings.SfxVolume;
            qualityText.text = settings.QualityName.ToUpper();
            fullscreenToggle.isOn = settings.Fullscreen;
            fpsToggle.isOn = settings.ShowFps;
            introToggle.isOn = settings.PlayIntro;
            DisplayManager display = DisplayManager.Instance;
            if (display != null && resolutionDropdown != null)
            {
                // Rebuild the list only when it changed (a different display or window).
                bool same = resolutionDropdown.options.Count == display.Options.Count;
                for (int i = 0; same && i < display.Options.Count; i++)
                    same = resolutionDropdown.options[i].text == display.OptionLabel(i);
                if (!same)
                {
                    resolutionDropdown.ClearOptions();
                    var labels = new System.Collections.Generic.List<string>();
                    for (int i = 0; i < display.Options.Count; i++) labels.Add(display.OptionLabel(i));
                    resolutionDropdown.AddOptions(labels);
                }
                resolutionDropdown.SetValueWithoutNotify(display.ScaleIndex);
                resolutionDropdown.RefreshShownValue();
                resolutionDropdown.interactable = !display.Detecting;
                resolutionDetail.text = display.DetailLabel();
                autoDetectButton.interactable = !display.Detecting;
            }
            refreshing = false;
        }
    }
}
