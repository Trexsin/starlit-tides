// MenuCamera: a slow, swaying view of the glowing bay behind the main menu.
//
// The camera sits out at sea and swings gently left and right (like a slow
// pendulum) around a point in the bay, so the village, lighthouse and moon
// stay in frame.
// It's a Cinemachine camera that takes over (high priority) only while the
// game is in the Menu state. A separate Volume adds depth of field so the
// bay looks soft and dreamy behind the title.

using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarlitTides
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class MenuCamera : MonoBehaviour
    {
        public Vector3 orbitCentre = new Vector3(-20f, 0f, 90f);
        public Vector3 lookAt = new Vector3(-16f, 10f, 100f);
        public float radius = 125f;
        public float height = 20f;
        [Tooltip("Centre of the swing, in degrees around the orbit centre (-67 = out at sea, south-east).")]
        public float baseAngle = -67f;
        [Tooltip("How far it swings each way, in degrees.")]
        public float swing = 14f;
        [Tooltip("Seconds for one full swing back and forth.")]
        public float swingPeriod = 60f;
        [Tooltip("Menu depth-of-field volume (faded in while in the menu).")]
        public Volume menuVolume;

        CinemachineCamera cam;

        void Awake()
        {
            cam = GetComponent<CinemachineCamera>();
        }

        void Update()
        {
            bool inMenu = GameManager.Instance != null && GameManager.Instance.State == GameState.Menu;
            cam.Priority.Enabled = true;
            cam.Priority.Value = inMenu ? 50 : 0;
            if (menuVolume != null)
                menuVolume.weight = Mathf.MoveTowards(menuVolume.weight, inMenu ? 1f : 0f, Time.unscaledDeltaTime);

            if (!inMenu) return;

            // Swing slowly around the bay and bob gently up and down.
            float angle = baseAngle + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / swingPeriod) * swing;
            float rad = angle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
            offset.y = height + Mathf.Sin(Time.unscaledTime * 0.2f) * 4f;
            transform.position = orbitCentre + offset;
            transform.LookAt(lookAt);
        }
    }
}
