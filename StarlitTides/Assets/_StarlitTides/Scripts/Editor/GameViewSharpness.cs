// GameViewSharpness: keeps the editor's Game view at full Retina resolution.
//
// On a Retina Mac the Game view can render at HALF resolution and stretch the
// image ("Low Resolution Aspect Ratios", or a zoom above 1x). The game then
// looks soft and pixelated in the editor even though the build is sharp.
// Every time scripts reload and every time you press Play, this turns that
// option off and resets the zoom to 1x, so Play mode shows the real image.
// Menu: Starlit Tides > Fix Game View Sharpness (to run it by hand).

#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace StarlitTides.EditorTools
{
    [InitializeOnLoad]
    public static class GameViewSharpness
    {
        static GameViewSharpness()
        {
            EditorApplication.delayCall += Apply;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) Apply();
            };
        }

        [MenuItem("Starlit Tides/Fix Game View Sharpness", priority = 41)]
        public static void Apply()
        {
            Type gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
            if (gameViewType == null) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo lowRes = gameViewType.GetProperty("lowResolutionForAspectRatios", flags);
            MethodInfo snapZoom = gameViewType.GetMethod("SnapZoom", flags, null, new[] { typeof(float) }, null);

            foreach (UnityEngine.Object view in Resources.FindObjectsOfTypeAll(gameViewType))
            {
                try
                {
                    if (lowRes != null && lowRes.CanWrite && (bool)lowRes.GetValue(view)) lowRes.SetValue(view, false);
                    if (snapZoom != null) snapZoom.Invoke(view, new object[] { 1f });
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Starlit Tides] Couldn't adjust the Game view: " + e.Message);
                }
            }
        }
    }
}
#endif
