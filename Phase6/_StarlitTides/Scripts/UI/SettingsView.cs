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
            refreshing = false;
        }
    }
}
