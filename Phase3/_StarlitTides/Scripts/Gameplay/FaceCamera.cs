// =============================================================================
//  FaceCamera - keeps a quad (orb halo) turned toward the camera (billboard)
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    /// <summary>Keeps a quad facing the camera (orb halos).</summary>
    public class FaceCamera : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam != null) transform.rotation = cam.transform.rotation;
        }
    }
}
