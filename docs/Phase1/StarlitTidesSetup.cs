// =============================================================================
//  STARLIT TIDES - Phase 1 project setup helper (Editor only)
// -----------------------------------------------------------------------------
//  Put this file in:  Assets/_StarlitTides/Scripts/Editor/StarlitTidesSetup.cs
//  (Any folder named "Editor" works. Unity keeps Editor scripts out of builds.)
//
//  Adds a "Starlit Tides" menu to the Unity menu bar with three commands:
//    1. Create Folder Structure   - makes every project folder in one click
//    2. Install Packages          - Cinemachine 3, Input System, ProBuilder, ...
//    3. Apply Player Settings     - names, resolution, Linear colour, etc.
//  Every command is safe to run more than once.
// =============================================================================
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace StarlitTides.EditorTools
{
    public static class StarlitTidesSetup
    {
        // Our own root folder. The underscore keeps it sorted above the folders
        // that downloaded asset packs create (Kenney, Quaternius, ...).
        private const string Root = "Assets/_StarlitTides";

        // Every folder the project uses, relative to Root.
        private static readonly string[] Folders =
        {
            "Scenes",
            "Scripts/Core",          // GameManager, HighScoreManager, ObjectPool
            "Scripts/Gameplay",      // BoatController, LightMeter, Orb, Obstacle, spawners
            "Scripts/Features",      // Extra features 1-6 (Combo, Starburst, Weather, ...)
            "Scripts/Bonus",         // Bonus features 7-11 (Whale, PhotoMode, ...)
            "Scripts/Environment",   // Lanterns, lighthouse, village life
            "Scripts/CameraFX",      // CameraShake, menu orbit, cinematic helpers
            "Scripts/UI",
            "Scripts/Audio",
            "Scripts/Editor",        // Editor-only tools like this file
            "Materials/Environment",
            "Materials/Gameplay",
            "Materials/VFX",
            "Shaders",               // Shader Graphs: ocean, sky, aurora, glow
            "Prefabs/Environment",
            "Prefabs/Gameplay",
            "Prefabs/VFX",
            "Prefabs/UI",
            "VFX/Textures",
            "Audio/Music",
            "Audio/SFX",
            "Audio/Ambience",
            "Audio/Mixers",
            "UI/Fonts",
            "UI/Sprites",
            "Models/Environment",
            "Models/Gameplay",
            "Textures",
            "Settings/URP",          // URP_Low / URP_Medium / URP_High assets
            "Settings/Volumes",      // post-processing Volume profiles
            "Settings/Input",
            "Timeline",              // intro cinematic
            "ThirdParty",            // move imported free packs here to stay tidy
        };

        // Packages to install. URP, Shader Graph and Timeline normally come with
        // the "Universal 3D" template; listing them again is harmless.
        // Cinemachine is pinned to the 3.x line: our scripts use the Cinemachine 3
        // API (namespace Unity.Cinemachine), which differs from 2.x.
        private static readonly string[] Packages =
        {
            "com.unity.render-pipelines.universal",
            "com.unity.shadergraph",
            "com.unity.inputsystem",
            "com.unity.cinemachine@3.1.8",
            "com.unity.timeline",
            "com.unity.splines",       // camera paths for menu orbit / intro flythrough
            "com.unity.probuilder",    // pier, bridge, stalls, simple buildings
            "com.unity.ugui",          // uGUI + TextMeshPro (merged in Unity 6)
            "com.unity.recorder",      // optional: record a demo video of the game
        };

        private static AddAndRemoveRequest _request;

        // ---------------------------------------------------------------------
        // 1. Folders
        // ---------------------------------------------------------------------
        [MenuItem("Starlit Tides/1. Create Folder Structure", priority = 1)]
        public static void CreateFolders()
        {
            int created = 0;
            foreach (string relative in Folders)
            {
                string full = Root + "/" + relative;
                if (AssetDatabase.IsValidFolder(full)) continue;

                // AssetDatabase.CreateFolder makes one level at a time,
                // so walk the path and create each missing parent.
                string parent = "Assets";
                foreach (string part in full.Substring("Assets/".Length).Split('/'))
                {
                    string next = parent + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(parent, part);
                        created++;
                    }
                    parent = next;
                }
            }

            // A short README so the structure explains itself in the Project window.
            string readme = Path.Combine(Root, "README.txt");
            if (!File.Exists(readme))
            {
                File.WriteAllText(readme,
                    "STARLIT TIDES - project root\n" +
                    "Scenes        StarlitBay (main scene), Sandbox_Ocean (shader testing)\n" +
                    "Scripts       Core / Gameplay / Features (1-6) / Bonus (7-11) / ...\n" +
                    "Shaders       Shader Graphs (Ocean, NightSky, Aurora, Glow)\n" +
                    "Settings      URP quality assets and post-processing Volume profiles\n" +
                    "ThirdParty    Imported free asset packs (Kenney, Quaternius, ...)\n");
            }

            AssetDatabase.Refresh();
            Debug.Log($"[Starlit Tides] Folder structure ready ({created} new folders) under {Root}.");
        }

        // ---------------------------------------------------------------------
        // 2. Packages
        // ---------------------------------------------------------------------
        [MenuItem("Starlit Tides/2. Install Packages", priority = 2)]
        public static void InstallPackages()
        {
            if (_request != null && !_request.IsCompleted)
            {
                Debug.LogWarning("[Starlit Tides] A package install is already running. Please wait.");
                return;
            }

            Debug.Log("[Starlit Tides] Installing packages... this can take a few minutes. " +
                      "Unity will recompile when it finishes.");
            _request = Client.AddAndRemove(Packages, null);
            EditorApplication.update += WatchPackageRequest;
        }

        private static void WatchPackageRequest()
        {
            if (_request == null || !_request.IsCompleted) return;
            EditorApplication.update -= WatchPackageRequest;

            if (_request.Status == StatusCode.Success)
            {
                Debug.Log("[Starlit Tides] Packages installed. Next: Window > TextMeshPro > " +
                          "Import TMP Essential Resources, then run '3. Apply Player Settings'.");
            }
            else
            {
                Debug.LogError("[Starlit Tides] Package install failed: " + _request.Error?.message +
                               "\nInstall the packages by hand in Window > Package Manager (see the Phase 1 guide).");
            }
            _request = null;
        }

        // ---------------------------------------------------------------------
        // 3. Player settings
        // ---------------------------------------------------------------------
        [MenuItem("Starlit Tides/3. Apply Player Settings", priority = 3)]
        public static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "Starlit Tides Team";
            PlayerSettings.productName = "Starlit Tides";

            // Linear colour space is required for correct HDR bloom and lighting.
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // Windows player: 1920x1080 borderless fullscreen by default.
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = false;   // pause the game when alt-tabbed
            PlayerSettings.visibleInBackground = false;

            // Keeps physics and animation smooth; VSync is set per quality level.
            Time.fixedDeltaTime = 1f / 60f;

            Debug.Log("[Starlit Tides] Player settings applied (name, 1920x1080, Linear colour). " +
                      "Check Edit > Project Settings > Player > Other Settings > Active Input Handling " +
                      "is 'Input System Package (New)' or 'Both'.");
        }
    }
}
#endif
