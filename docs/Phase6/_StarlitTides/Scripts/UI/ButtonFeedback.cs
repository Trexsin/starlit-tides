// ButtonFeedback: makes menu buttons feel alive.
// When a button is hovered or selected (keyboard/gamepad) it grows a little
// and its text turns gold; clicking plays a soft click sound.

using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StarlitTides
{
    public class ButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public Color normalColor = new Color(0.85f, 0.93f, 1f);
        public Color highlightColor = new Color(1f, 0.82f, 0.5f);
        public float highlightScale = 1.06f;

        TextMeshProUGUI label;
        bool highlighted;

        void Awake()
        {
            label = GetComponentInChildren<TextMeshProUGUI>();
            Button button = GetComponent<Button>();
            if (button != null) button.onClick.AddListener(PlayClick);
        }

        void OnEnable()
        {
            highlighted = false;
            transform.localScale = Vector3.one;
            if (label != null) label.color = normalColor;
        }

        void Update()
        {
            float target = highlighted ? highlightScale : 1f;
            float s = Mathf.MoveTowards(transform.localScale.x, target, Time.unscaledDeltaTime * 1.5f);
            transform.localScale = new Vector3(s, s, 1f);
        }

        void SetHighlight(bool on)
        {
            highlighted = on;
            if (label != null) label.color = on ? highlightColor : normalColor;
        }

        public void OnPointerEnter(PointerEventData e) { SetHighlight(true); }
        public void OnPointerExit(PointerEventData e) { SetHighlight(false); }
        public void OnSelect(BaseEventData e) { SetHighlight(true); }
        public void OnDeselect(BaseEventData e) { SetHighlight(false); }

        void PlayClick()
        {
            if (AudioManager.Instance != null) AudioManager.Instance.PlayClick();
        }
    }
}
