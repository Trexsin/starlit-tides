// FaceCamera: keeps a flat quad turned toward the camera (a "billboard").
// Used for the soft glow halos around the orbs and the boat's candle.

using UnityEngine;

namespace StarlitTides
{
    public class FaceCamera : MonoBehaviour
    {
        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
