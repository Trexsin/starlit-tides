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
        public TextMeshProUGUI resolutionText;
        public TextMeshProUGUI resolutionDetail;
        public Button resolutionLeft;
        public Button resolutionRight;
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
            if (resolutionLeft != null) resolutionLeft.onClick.AddListener(() => ChangeResolution(-1));
            if (resolutionRight != null) resolutionRight.onClick.AddListener(() => ChangeResolution(1));
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

        void ChangeResolution(int direction)
        {
            if (DisplayManager.Instance == null) return;
            DisplayManager.Instance.StepResolution(direction);
            Refresh();
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
            if (display != null && resolutionText != null)
            {
                resolutionText.text = display.ResolutionLabel();
                resolutionDetail.text = display.DetailLabel();
                resolutionLeft.interactable = display.ScaleIndex < DisplayManager.Scales.Length - 1;
                resolutionRight.interactable = display.ScaleIndex > 0;
                autoDetectButton.interactable = !display.Detecting;
            }
            refreshing = false;
        }
    }
}
