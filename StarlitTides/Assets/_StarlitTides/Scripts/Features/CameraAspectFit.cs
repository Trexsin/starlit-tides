// CameraAspectFit: keeps the gameplay camera's view the same width on any screen.
//
// Unity's field of view is vertical. On a wider screen (21:9) a fixed vertical
// FOV shows more sea at the sides, which is exactly the empty space Phase 8
// removes. So we fix the *horizontal* FOV instead (the one a 16:9 screen
// gets from referenceVerticalFov) and work out the vertical FOV from it:
//
//     vertical = 2 * atan( tan(horizontal / 2) / aspect )
//
// Taller screens (4:3, 16:10) get a slightly taller view, up to maxVerticalFov.
// Dashing adds a small FOV kick, which reads as a burst of speed.
// Phase 9: the view also widens as the tide gets faster (SpeedFx.FovBoost).

using Unity.Cinemachine;
using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(CinemachineCamera))]
    public class CameraAspectFit : MonoBehaviour
    {
        [Tooltip("Vertical FOV on a 16:9 screen. The horizontal FOV this gives is kept on every screen.")]
        public float referenceVerticalFov = 48f;
        public float minVerticalFov = 36f;
        public float maxVerticalFov = 62f;

        [Header("Dash kick")]
        public float dashFovKick = 4f;
        public float kickSharpness = 10f;

        CinemachineCamera cam;
        float kick;

        void Awake()
        {
            cam = GetComponent<CinemachineCamera>();
        }

        void LateUpdate()
        {
            Camera main = Camera.main;
            float aspect = main != null ? main.aspect : 16f / 9f;
            if (aspect <= 0.01f) aspect = 16f / 9f;

            float halfH = Mathf.Atan(Mathf.Tan(referenceVerticalFov * 0.5f * Mathf.Deg2Rad) * (16f / 9f));
            float vertical = 2f * Mathf.Atan(Mathf.Tan(halfH) / aspect) * Mathf.Rad2Deg;
            vertical = Mathf.Clamp(vertical, minVerticalFov, maxVerticalFov);

            BoatController boat = BoatController.Instance;
            float targetKick = boat != null && boat.IsDashing ? dashFovKick : 0f;
            kick = Mathf.Lerp(kick, targetKick, 1f - Mathf.Exp(-kickSharpness * Time.deltaTime));

            // (With SpeedFx in the scene, the dash punch comes from its Kick instead.)
            float speed = 0f;
            if (SpeedFx.Instance != null) { speed = SpeedFx.Instance.FovBoost; kick = 0f; }

            LensSettings lens = cam.Lens;
            lens.FieldOfView = Mathf.Min(vertical + kick + speed, maxVerticalFov + 10f);
            cam.Lens = lens;
        }
    }
}
