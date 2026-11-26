// =============================================================================
//  DebugHUD - temporary on-screen info for Phase 3 play-testing
// -----------------------------------------------------------------------------
//  Uses Unity's immediate-mode GUI so it needs no Canvas or fonts.
//  Shows the light meter, score, distance, speed and dash cooldown, plus the
//  pause and game-over messages. Phase 6 replaces it with the real
//  TextMeshPro HUD; then just disable or delete this object.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class DebugHUD : MonoBehaviour
    {
        public LightMeter lightMeter;
        public BoatController boat;

        private GUIStyle _label, _big;
        private Texture2D _white;

        private void OnGUI()
        {
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label) { fontSize = 18 };
                _label.normal.textColor = new Color(0.85f, 0.95f, 1f);
                _big = new GUIStyle(_label) { fontSize = 40, alignment = TextAnchor.MiddleCenter };
                _white = Texture2D.whiteTexture;
            }

            GameManager gm = GameManager.Instance;
            ScoreManager sm = ScoreManager.Instance;
            DifficultyManager dm = DifficultyManager.Instance;
            OceanController oc = OceanController.Instance;

            // light meter bar
            float v = lightMeter != null ? lightMeter.SmoothValue : 0f;
            Rect bar = new Rect(24, 24, 320, 18);
            GUI.color = new Color(0f, 0f, 0f, 0.5f); GUI.DrawTexture(bar, _white);
            GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), new Color(0.3f, 0.95f, 1f), v);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(v), bar.height), _white);
            GUI.color = Color.white;
            GUI.Label(new Rect(24, 44, 600, 26), $"LIGHT {Mathf.RoundToInt(v * 100)}%", _label);

            if (sm != null)
            {
                GUI.Label(new Rect(24, 70, 600, 26), $"SCORE {sm.Score:N0}    ORBS {sm.OrbsCollected}    DISTANCE {sm.Distance:0} m", _label);
                GUI.Label(new Rect(24, 96, 600, 26), $"MULTIPLIER x{sm.TotalMultiplier:0.0}", _label);
            }
            if (dm != null && oc != null)
                GUI.Label(new Rect(24, 122, 600, 26), $"SPEED {oc.tideSpeed:0.0} m/s    DIFFICULTY {dm.Level * 100f:0}%", _label);
            if (boat != null)
                GUI.Label(new Rect(24, 148, 600, 26), boat.DashCooldown01 >= 1f ? "DASH READY [Space]" : "dash recharging...", _label);

            if (gm == null) return;
            Rect centre = new Rect(0, Screen.height * 0.35f, Screen.width, 60);
            if (gm.State == GameState.Paused)
                GUI.Label(centre, "PAUSED  -  Esc to resume", _big);
            else if (gm.State == GameState.GameOver)
            {
                GUI.Label(centre, "The tide has gone dark...", _big);
                if (sm != null) GUI.Label(new Rect(0, centre.y + 60, Screen.width, 40), $"Score {sm.Score:N0}   -   press R to try again", new GUIStyle(_big) { fontSize = 22 });
            }
            else if (gm.InGracePeriod)
                GUI.Label(centre, "Collect the glowing plankton - dodge the dark", new GUIStyle(_big) { fontSize = 26 });
        }
    }
}
