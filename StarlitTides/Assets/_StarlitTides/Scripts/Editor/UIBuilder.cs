// UIBuilder: builds the whole user interface and the final polish with one click.
// Menu: Starlit Tides > 8. Build UI & Polish
//
// Creates:
//   - TextMeshPro font assets from Cinzel (titles) and Quicksand (HUD, text)
//   - the HUD (circular light meter, score, combo, Starburst timer, pop-ups)
//   - Main Menu, How to Play, Settings, Pause and Game Over screens + fades
//   - the menu camera that orbits the bay, with depth of field
//   - Low / Medium / High quality levels (three URP assets) and the SettingsManager
// It also switches off the temporary DebugHUD.
// Running it again rebuilds "== STARLIT UI ==".

#if UNITY_EDITOR
using System.IO;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace StarlitTides.EditorTools
{
    public static class UIBuilder
    {
        const string RootName = "== STARLIT UI ==";
        const string UiFolder = "Assets/_StarlitTides/UI";
        const string Generated = "Assets/_StarlitTides/Generated/UI";
        const string TmpPackage = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";

        // Palette
        static readonly Color Gold = new Color(1f, 0.82f, 0.5f);
        static readonly Color Cyan = new Color(0.55f, 0.92f, 1f);
        static readonly Color TextColor = new Color(0.85f, 0.93f, 1f);
        static readonly Color Muted = new Color(0.56f, 0.64f, 0.78f);
        static readonly Color PanelColor = new Color(0.02f, 0.04f, 0.1f, 0.82f);

        static TMP_FontAsset titleFont, bodyFont;
        static Material titleGlow;
        static Sprite rounded, ringSprite, glowSprite, gradientSprite;
        static DefaultControls.Resources uiResources;
        static UIManager ui;

        [MenuItem("Starlit Tides/8. Build UI & Polish", priority = 24)]
        public static void BuildMenu()
        {
            if (Object.FindAnyObjectByType<HighScoreManager>() == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Build the bonus features first (Starlit Tides > 7. Build Bonus Features).", "OK");
                return;
            }
            if (!EnsureTextMeshPro()) return;
            bool ok = Application.isBatchMode || EditorUtility.DisplayDialog("Build Starlit Tides UI",
                "Builds the menus, HUD, settings, quality levels and menu camera.\n\n'" + RootName + "' will be rebuilt if it already exists.",
                "Build", "Cancel");
            if (ok) Build();
        }

        // TextMeshPro needs its "Essential Resources" in the project (default settings and shaders).
        public static bool EnsureTextMeshPro()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;
            AssetDatabase.ImportPackage(TmpPackage, false);
            AssetDatabase.Refresh();
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) return true;
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("Starlit Tides", "TextMeshPro resources are being imported.\nWhen Unity finishes, run 8. Build UI & Polish again.", "OK");
            Debug.LogWarning("[Starlit Tides] Importing TMP Essential Resources - run the UI builder again afterwards.");
            return false;
        }

        public static void Build()
        {
            MakeFolder(Generated);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            GameObject root = new GameObject(RootName);

            LoadResources();
            SetupQualityLevels();

            // Managers
            root.AddComponent<SettingsManager>();
            ui = root.AddComponent<UIManager>();
            MakeEventSystem(root.transform);

            // Canvases: HUD below, menus above, the fade on top of everything.
            Canvas hud = MakeCanvas("HUD Canvas", root.transform, 5);
            Canvas menus = MakeCanvas("Menu Canvas", root.transform, 10);
            Canvas fadeCanvas = MakeCanvas("Fade Canvas", root.transform, 100);

            ui.hudCanvas = hud;
            BuildHud(hud.transform);
            ui.menuScreen = BuildMainMenu(menus.transform);
            ui.howToPlayScreen = BuildHowToPlay(menus.transform);
            ui.settingsScreen = BuildSettings(menus.transform);
            ui.pauseScreen = BuildPause(menus.transform);
            ui.gameOverScreen = BuildGameOver(menus.transform);
            ui.fader = BuildFader(fadeCanvas.transform);

            BuildMenuCamera(root.transform);
            WireUpScene(hud);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Starlit Tides] UI built. Press Play: the main menu appears with the camera circling the bay.");
        }

        // ==============================================================
        // Assets: fonts, sprites
        // ==============================================================
        static void MakeFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            MakeFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void LoadResources()
        {
            titleFont = MakeFontAsset("Cinzel", 110);
            bodyFont = MakeFontAsset("Quicksand", 90);

            // A glowing copy of the title font material (TextMeshPro's built-in glow effect).
            titleGlow = new Material(titleFont.material);
            titleGlow.name = "Cinzel Glow";
            titleGlow.EnableKeyword("GLOW_ON");
            titleGlow.SetColor("_GlowColor", new Color(1f, 0.7f, 0.3f, 0.55f));
            titleGlow.SetFloat("_GlowOuter", 0.7f);
            titleGlow.SetFloat("_GlowInner", 0.1f);
            titleGlow.SetFloat("_GlowPower", 0.6f);
            titleGlow.SetFloat("_GlowOffset", 0f);
            SaveAsset(titleGlow, Generated + "/M_CinzelGlow.mat");

            rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            uiResources = new DefaultControls.Resources();
            uiResources.standard = rounded;
            uiResources.background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd");
            uiResources.inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd");
            uiResources.knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            uiResources.checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd");

            ringSprite = MakeSprite("T_Ring", 256, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                return Mathf.Clamp01((d - 0.78f) / 0.02f) * Mathf.Clamp01((0.98f - d) / 0.02f);   // a crisp ring
            });
            glowSprite = MakeSprite("T_RingGlow", 256, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float fadeOut = Mathf.Clamp01((1f - d) / 0.2f);                                  // reach zero before the edge
                return Mathf.Exp(-Mathf.Pow((d - 0.7f) / 0.18f, 2f)) * fadeOut;                  // soft halo
            });
            gradientSprite = MakeSprite("T_SideGradient", 64, (x, y) => Mathf.Pow(Mathf.Clamp01(0.5f - x * 0.5f), 1.5f));
        }

        static TMP_FontAsset MakeFontAsset(string name, int pointSize)
        {
            string path = Generated + "/" + name + " SDF.asset";
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) return existing;

            Font font = AssetDatabase.LoadAssetAtPath<Font>(UiFolder + "/Fonts/" + name + ".ttf");
            if (font == null) throw new System.Exception("[Starlit Tides] Font " + name + ".ttf missing in " + UiFolder + "/Fonts");

            // Dynamic SDF font: glyphs are added to the atlas when first used.
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font, pointSize, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            asset.name = name + " SDF";
            AssetDatabase.CreateAsset(asset, path);
            asset.atlasTextures[0].name = name + " Atlas";
            AssetDatabase.AddObjectToAsset(asset.atlasTextures[0], asset);
            asset.material.name = name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            asset.TryAddCharacters(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~•…");
            EditorUtility.SetDirty(asset);
            return asset;
        }

        // Makes a white sprite whose alpha comes from a function of (x, y) in -1..1.
        static Sprite MakeSprite(string name, int size, System.Func<float, float, float> alpha)
        {
            string path = Generated + "/" + name + ".png";
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(u, v))));
                }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;   // Unity 6 defaults new sprites to "Multiple"
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static T SaveAsset<T>(T asset, string path) where T : Object
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ==============================================================
        // Small UI helpers
        // ==============================================================
        static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        static RectTransform Stretch(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static TextMeshProUGUI Label(string name, Transform parent, string text, TMP_FontAsset font, float size, Color color,
                                     TextAlignmentOptions align, Vector2 anchor, Vector2 position, Vector2 box)
        {
            RectTransform rt = Rect(name, parent, anchor, position, box);
            TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.raycastTarget = false;
            return t;
        }

        static Image Picture(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rt = Rect(name, parent, anchor, position, size);
            Image img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        static Image Panel(string name, Transform parent, Vector2 size)
        {
            Image img = Picture(name, parent, rounded, PanelColor, new Vector2(0.5f, 0.5f), Vector2.zero, size);
            img.type = Image.Type.Sliced;
            return img;
        }

        static CanvasGroup Screen(string name, Transform parent)
        {
            RectTransform rt = Stretch(name, parent);
            CanvasGroup group = rt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }

        // A dark translucent full-screen layer behind pause / game over.
        static void Dim(Transform parent, float alpha)
        {
            Image img = Stretch("Dim", parent).gameObject.AddComponent<Image>();
            img.color = new Color(0.01f, 0.02f, 0.05f, alpha);
        }

        static Button MakeButton(string label, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, UnityAction action, float fontSize = 36f)
        {
            RectTransform rt = Rect(label + " Button", parent, anchor, position, size);
            Image bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = rounded;
            bg.type = Image.Type.Sliced;
            bg.color = Color.white;   // the button's colour states below do the tinting
            Button button = rt.gameObject.AddComponent<Button>();
            // Dark "glass" so the label stays readable over the bright sea;
            // it warms to a deep gold when hovered or selected.
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.04f, 0.09f, 0.18f, 0.8f);
            colors.highlightedColor = new Color(0.24f, 0.18f, 0.07f, 0.92f);
            colors.selectedColor = new Color(0.24f, 0.18f, 0.07f, 0.92f);
            colors.pressedColor = new Color(0.38f, 0.28f, 0.1f, 0.95f);
            colors.disabledColor = new Color(0.04f, 0.09f, 0.18f, 0.4f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            // A thin cyan edge.
            Outline edge = rt.gameObject.AddComponent<Outline>();
            edge.effectColor = new Color(0.55f, 0.92f, 1f, 0.35f);
            edge.effectDistance = new Vector2(1.5f, -1.5f);

            TextMeshProUGUI text = Label("Label", rt, label, titleFont, fontSize, TextColor, TextAlignmentOptions.Center,
                                         new Vector2(0.5f, 0.5f), Vector2.zero, size);
            text.characterSpacing = 6f;
            rt.gameObject.AddComponent<ButtonFeedback>();
            if (action != null) UnityEventTools.AddPersistentListener(button.onClick, action);   // saved with the scene
            return button;
        }

        static Slider MakeSlider(string label, Transform parent, float y)
        {
            Label(label + " Label", parent, label, bodyFont, 30, TextColor, TextAlignmentOptions.Left, new Vector2(0.5f, 0.5f), new Vector2(-220, y), new Vector2(300, 50));
            GameObject go = DefaultControls.CreateSlider(uiResources);
            go.name = label + " Slider";
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(150, y);
            rt.sizeDelta = new Vector2(380, 24);
            Slider slider = go.GetComponent<Slider>();
            slider.minValue = 0f;
            slider.maxValue = 1f;
            // Recolour: dark track, cyan fill, gold knob.
            foreach (Image img in go.GetComponentsInChildren<Image>())
            {
                if (img.name == "Background") img.color = new Color(1f, 1f, 1f, 0.12f);
                if (img.name == "Fill") img.color = Cyan;
                if (img.name == "Handle") img.color = Gold;
            }
            return slider;
        }

        static Toggle MakeToggle(string label, Transform parent, float y)
        {
            GameObject go = DefaultControls.CreateToggle(uiResources);
            go.name = label + " Toggle";
            go.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(-40, y);
            rt.sizeDelta = new Vector2(520, 44);

            // Swap the default legacy label for a TextMeshPro one.
            Text legacy = go.GetComponentInChildren<Text>();
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
            Label("Label", rt, label, bodyFont, 30, TextColor, TextAlignmentOptions.Left, new Vector2(0f, 0.5f), new Vector2(50, 0), new Vector2(460, 44));

            Transform background = go.transform.Find("Background");
            if (background != null)
            {
                RectTransform bg = (RectTransform)background;
                bg.sizeDelta = new Vector2(34, 34);
                bg.anchoredPosition = new Vector2(17, -22);   // centred on the 44px row
                background.GetComponent<Image>().color = new Color(0.04f, 0.09f, 0.18f, 0.9f);
                Outline boxEdge = background.gameObject.AddComponent<Outline>();
                boxEdge.effectColor = new Color(0.55f, 0.92f, 1f, 0.6f);
                boxEdge.effectDistance = new Vector2(1.5f, -1.5f);
                RectTransform check = (RectTransform)background.Find("Checkmark");
                if (check != null)
                {
                    // A filled cyan square reads better than the built-in (dark grey) tick.
                    check.sizeDelta = new Vector2(20, 20);
                    Image checkImage = check.GetComponent<Image>();
                    checkImage.sprite = rounded;
                    checkImage.type = Image.Type.Sliced;
                    checkImage.color = Cyan;
                }
            }
            return go.GetComponent<Toggle>();
        }

        // ==============================================================
        // Canvas + event system
        // ==============================================================
        static Canvas MakeCanvas(string name, Transform parent, int order)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            CanvasScaler scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        static void MakeEventSystem(Transform parent)
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem");
            go.transform.SetParent(parent, false);
            go.AddComponent<EventSystem>();
            // The new Input System's UI module: mouse, keyboard arrows/Enter and gamepad all work.
            InputSystemUIInputModule module = go.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }

        // ==============================================================
        // HUD
        // ==============================================================
        static void BuildHud(Transform canvas)
        {
            HudView hud = canvas.gameObject.AddComponent<HudView>();
            hud.lightMeter = Object.FindAnyObjectByType<LightMeter>();

            // Circular light meter, top-left.
            Vector2 topLeft = new Vector2(0f, 1f);
            RectTransform meter = Rect("Light Meter", canvas, topLeft, new Vector2(60, -50), new Vector2(170, 170));
            hud.meterGlow = Picture("Glow", meter, glowSprite, Cyan, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(250, 250));
            Picture("Track", meter, ringSprite, new Color(1f, 1f, 1f, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170, 170));
            Image fill = Picture("Fill", meter, ringSprite, Cyan, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170, 170));
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = false;
            hud.meterFill = fill;
            hud.meterText = Label("Percent", meter, "100%", bodyFont, 46, TextColor, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 6), new Vector2(160, 60));
            Label("Caption", meter, "LIGHT", titleFont, 18, Muted, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, -34), new Vector2(160, 30)).characterSpacing = 8f;

            // Score, top-right.
            Vector2 topRight = new Vector2(1f, 1f);
            hud.scoreText = Label("Score", canvas, "0", bodyFont, 66, TextColor, TextAlignmentOptions.Right, topRight, new Vector2(-60, -40), new Vector2(600, 80));
            hud.bestText = Label("Best", canvas, "BEST 0", titleFont, 22, Muted, TextAlignmentOptions.Right, topRight, new Vector2(-62, -120), new Vector2(600, 34));
            hud.bestText.characterSpacing = 6f;

            // Combo, top-centre.
            Vector2 top = new Vector2(0.5f, 1f);
            RectTransform comboRoot = Rect("Combo", canvas, top, new Vector2(0, -40), new Vector2(600, 90));
            hud.comboGroup = comboRoot.gameObject.AddComponent<CanvasGroup>();
            hud.comboGroup.alpha = 0f;
            hud.comboText = Label("Text", comboRoot, "x1.0", titleFont, 44, Gold, TextAlignmentOptions.Center, top, Vector2.zero, new Vector2(600, 60));
            hud.comboText.fontSharedMaterial = titleGlow;
            Picture("Bar Track", comboRoot, rounded, new Color(1f, 1f, 1f, 0.15f), top, new Vector2(0, -66), new Vector2(260, 6));
            Image comboBar = Picture("Bar", comboRoot, rounded, Gold, top, new Vector2(0, -66), new Vector2(260, 6));
            comboBar.type = Image.Type.Filled;
            comboBar.fillMethod = Image.FillMethod.Horizontal;
            hud.comboBar = comboBar;

            // Starburst timer, under the combo.
            RectTransform starRoot = Rect("Starburst", canvas, top, new Vector2(0, -140), new Vector2(600, 70));
            hud.starburstGroup = starRoot.gameObject.AddComponent<CanvasGroup>();
            hud.starburstGroup.alpha = 0f;
            Label("Text", starRoot, "STARBURST  x2", titleFont, 30, new Color(1f, 0.85f, 0.45f), TextAlignmentOptions.Center, top, Vector2.zero, new Vector2(600, 40)).characterSpacing = 6f;
            Image starBar = Picture("Bar", starRoot, rounded, new Color(1f, 0.8f, 0.35f), top, new Vector2(0, -48), new Vector2(320, 6));
            starBar.type = Image.Type.Filled;
            starBar.fillMethod = Image.FillMethod.Horizontal;
            hud.starburstBar = starBar;

            // Pop-ups, info line and FPS.
            hud.popupText = Label("Popup", canvas, "", titleFont, 50, TextColor, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 220), new Vector2(1400, 80));
            hud.popupText.fontSharedMaterial = titleGlow;
            Image infoBack = Picture("Info Backing", canvas, rounded, new Color(0.02f, 0.04f, 0.1f, 0.55f), new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(560, 40));
            infoBack.type = Image.Type.Sliced;
            hud.infoText = Label("Info", canvas, "", bodyFont, 22, TextColor, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0, 30), new Vector2(1200, 34));
            hud.fpsText = Label("FPS", canvas, "", bodyFont, 22, Muted, TextAlignmentOptions.Left, new Vector2(0f, 0f), new Vector2(30, 30), new Vector2(200, 34));
        }

        // ==============================================================
        // Screens
        // ==============================================================
        static CanvasGroup BuildMainMenu(Transform canvas)
        {
            CanvasGroup screen = Screen("Main Menu", canvas);
            Transform t = screen.transform;

            // A soft dark gradient on the left so the title stays readable over the bright bay.
            Image shade = Stretch("Shade", t).gameObject.AddComponent<Image>();
            shade.sprite = gradientSprite;
            shade.color = new Color(0.01f, 0.02f, 0.06f, 0.7f);
            shade.raycastTarget = false;

            Vector2 left = new Vector2(0f, 0.5f);
            TextMeshProUGUI title = Label("Title", t, "STARLIT TIDES", titleFont, 112, new Color(1f, 0.9f, 0.72f), TextAlignmentOptions.Left, left, new Vector2(150, 250), new Vector2(1200, 150));
            title.fontSharedMaterial = titleGlow;
            title.characterSpacing = 4f;
            Label("Subtitle", t, "a bioluminescent night voyage", bodyFont, 32, Cyan, TextAlignmentOptions.Left, left, new Vector2(158, 150), new Vector2(1000, 50)).characterSpacing = 4f;

            Vector2 size = new Vector2(420, 70);
            ui.menuFirst = MakeButton("PLAY", t, left, new Vector2(150, 30), size, ui.Play, 40);
            MakeButton("HOW TO PLAY", t, left, new Vector2(150, -60), size, ui.OpenHowToPlay);
            MakeButton("SETTINGS", t, left, new Vector2(150, -150), size, ui.OpenSettings);
            MakeButton("QUIT", t, left, new Vector2(150, -240), size, ui.Quit);

            Label("Credits", t, "Fonts: Cinzel & Quicksand (SIL Open Font License)  •  Made with Unity 6 URP",
                  bodyFont, 18, Muted, TextAlignmentOptions.Right, new Vector2(1f, 0f), new Vector2(-40, 24), new Vector2(1000, 30));
            return screen;
        }

        static CanvasGroup BuildHowToPlay(Transform canvas)
        {
            CanvasGroup screen = Screen("How To Play", canvas);
            Transform t = screen.transform;
            Dim(t, 0.4f);
            Panel("Panel", t, new Vector2(1280, 820));
            Vector2 c = new Vector2(0.5f, 0.5f);
            Label("Title", t, "HOW TO PLAY", titleFont, 60, Gold, TextAlignmentOptions.Center, c, new Vector2(0, 330), new Vector2(1000, 80)).fontSharedMaterial = titleGlow;

            Label("Controls", t,
                "<color=#FFD27A>CONTROLS</color>\n" +
                "WASD / Arrows / left stick    steer\n" +
                "Space / A    dash (brief invincibility)\n" +
                "Esc / Start    pause\n" +
                "P / Select    photo mode\n\n" +
                "<color=#FFD27A>THE GOAL</color>\n" +
                "Your paper boat's light slowly fades. Collect the glowing plankton orbs to keep it alive, and dodge rocks, drifting logs and jellyfish. When the light runs out, the tide goes dark.",
                bodyFont, 28, TextColor, TextAlignmentOptions.TopLeft, c, new Vector2(-300, -10), new Vector2(560, 560));

            Label("Features", t,
                "<color=#FFD27A>TIPS</color>\n" +
                "• Grab orbs in quick succession for a <color=#FFD27A>combo</color>: up to x6 score\n" +
                "• Slip past obstacles closely for a <color=#7FE8FF>near miss</color> bonus\n" +
                "• Catch the reflection of a <color=#FFD27A>golden shooting star</color> for Starburst: x2 points\n" +
                "• The lighthouse beam reveals <color=#7FE8FF>hidden orbs</color>\n" +
                "• Storms, auroras and a <color=#7FE8FF>giant glowing whale</color> appear as you sail on. Find the gap in its waves!",
                bodyFont, 28, TextColor, TextAlignmentOptions.TopLeft, c, new Vector2(320, -10), new Vector2(600, 560));

            ui.howToPlayFirst = MakeButton("BACK", t, c, new Vector2(0, -350), new Vector2(320, 64), ui.BackToMenu);
            return screen;
        }

        static CanvasGroup BuildSettings(Transform canvas)
        {
            CanvasGroup screen = Screen("Settings", canvas);
            Transform t = screen.transform;
            Dim(t, 0.4f);
            Panel("Panel", t, new Vector2(980, 860));
            Vector2 c = new Vector2(0.5f, 0.5f);
            Label("Title", t, "SETTINGS", titleFont, 60, Gold, TextAlignmentOptions.Center, c, new Vector2(0, 350), new Vector2(800, 80)).fontSharedMaterial = titleGlow;

            SettingsView view = t.gameObject.AddComponent<SettingsView>();
            ui.settingsView = view;
            view.masterSlider = MakeSlider("Master volume", t, 240);
            view.musicSlider = MakeSlider("Music", t, 170);
            view.sfxSlider = MakeSlider("Effects", t, 100);

            Label("Quality Label", t, "Graphics quality", bodyFont, 30, TextColor, TextAlignmentOptions.Left, c, new Vector2(-220, 20), new Vector2(300, 50));
            view.qualityLeft = MakeButton("<", t, c, new Vector2(0, 20), new Vector2(60, 50), null, 30);
            view.qualityText = Label("Quality", t, "HIGH", titleFont, 30, Gold, TextAlignmentOptions.Center, c, new Vector2(150, 20), new Vector2(220, 50));
            view.qualityRight = MakeButton(">", t, c, new Vector2(300, 20), new Vector2(60, 50), null, 30);

            view.fullscreenToggle = MakeToggle("Fullscreen", t, -60);
            view.fpsToggle = MakeToggle("Show FPS counter", t, -120);
            view.introToggle = MakeToggle("Play the intro cinematic", t, -180);

            Label("Hint", t, "Keyboard: WASD / arrows, Space dash, Esc pause, P photo mode   •   Gamepad supported",
                  bodyFont, 20, Muted, TextAlignmentOptions.Center, c, new Vector2(0, -255), new Vector2(900, 40));
            ui.settingsFirst = MakeButton("BACK", t, c, new Vector2(0, -345), new Vector2(320, 64), ui.CloseSettings);
            return screen;
        }

        static CanvasGroup BuildPause(Transform canvas)
        {
            CanvasGroup screen = Screen("Pause", canvas);
            Transform t = screen.transform;
            Dim(t, 0.6f);
            Vector2 c = new Vector2(0.5f, 0.5f);
            Label("Title", t, "PAUSED", titleFont, 96, new Color(1f, 0.9f, 0.72f), TextAlignmentOptions.Center, c, new Vector2(0, 230), new Vector2(900, 130)).fontSharedMaterial = titleGlow;
            Vector2 size = new Vector2(400, 70);
            ui.pauseFirst = MakeButton("RESUME", t, c, new Vector2(0, 60), size, ui.Resume, 40);
            MakeButton("SETTINGS", t, c, new Vector2(0, -30), size, ui.OpenSettings);
            MakeButton("MAIN MENU", t, c, new Vector2(0, -120), size, ui.MainMenu);
            Label("Hint", t, "Press P for photo mode", bodyFont, 24, Muted, TextAlignmentOptions.Center, c, new Vector2(0, -220), new Vector2(800, 40));
            return screen;
        }

        static CanvasGroup BuildGameOver(Transform canvas)
        {
            CanvasGroup screen = Screen("Game Over", canvas);
            Transform t = screen.transform;
            Dim(t, 0.55f);
            Vector2 c = new Vector2(0.5f, 0.5f);
            GameOverView view = t.gameObject.AddComponent<GameOverView>();
            ui.gameOverView = view;

            Label("Title", t, "The tide has gone dark...", titleFont, 72, new Color(1f, 0.9f, 0.72f), TextAlignmentOptions.Center, c, new Vector2(0, 380), new Vector2(1600, 100)).fontSharedMaterial = titleGlow;
            view.newBestText = Label("New Best", t, "NEW HIGH SCORE!", titleFont, 36, Gold, TextAlignmentOptions.Center, c, new Vector2(0, 305), new Vector2(800, 50));
            view.newBestText.fontSharedMaterial = titleGlow;

            // Left: this run.
            Image left = Panel("Stats Panel", t, new Vector2(700, 560));
            ((RectTransform)left.transform).anchoredPosition = new Vector2(-380, -10);
            view.statsText = Label("Stats", left.transform, "", bodyFont, 34, TextColor, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 1f), new Vector2(0, -40), new Vector2(600, 300));
            Label("Name Label", left.transform, "Your name for the world leaderboard", bodyFont, 24, Muted, TextAlignmentOptions.Left, new Vector2(0.5f, 0f), new Vector2(0, 165), new Vector2(600, 34));

            TMP_DefaultControls.Resources tmpResources = new TMP_DefaultControls.Resources();
            tmpResources.standard = rounded;
            tmpResources.inputField = uiResources.inputField;
            GameObject input = TMP_DefaultControls.CreateInputField(tmpResources);
            input.name = "Name Input";
            input.transform.SetParent(left.transform, false);
            RectTransform inputRt = (RectTransform)input.transform;
            inputRt.anchorMin = inputRt.anchorMax = inputRt.pivot = new Vector2(0.5f, 0f);
            inputRt.anchoredPosition = new Vector2(-110, 95);
            inputRt.sizeDelta = new Vector2(380, 56);
            input.GetComponent<Image>().color = new Color(0.04f, 0.09f, 0.18f, 0.85f);
            Outline inputEdge = input.AddComponent<Outline>();
            inputEdge.effectColor = new Color(0.55f, 0.92f, 1f, 0.35f);
            inputEdge.effectDistance = new Vector2(1.5f, -1.5f);
            TMP_InputField field = input.GetComponent<TMP_InputField>();
            field.characterLimit = 16;
            field.textComponent.font = bodyFont;
            field.textComponent.fontSize = 28;
            field.textComponent.color = TextColor;
            TextMeshProUGUI placeholder = (TextMeshProUGUI)field.placeholder;
            placeholder.font = bodyFont;
            placeholder.fontSize = 28;
            placeholder.text = "Enter name...";
            view.nameInput = field;
            view.submitButton = MakeButton("SUBMIT", left.transform, new Vector2(0.5f, 0f), new Vector2(200, 95), new Vector2(200, 56), null, 28);
            view.statusText = Label("Status", left.transform, "", bodyFont, 24, TextColor, TextAlignmentOptions.Left, new Vector2(0.5f, 0f), new Vector2(0, 40), new Vector2(600, 34));

            // Right: world top 10.
            Image right = Panel("Leaderboard Panel", t, new Vector2(560, 560));
            ((RectTransform)right.transform).anchoredPosition = new Vector2(330, -10);
            Label("Top Title", right.transform, "WORLD TOP 10", titleFont, 36, Gold, TextAlignmentOptions.Center, new Vector2(0.5f, 1f), new Vector2(0, -30), new Vector2(500, 50));
            view.topScoresText = Label("Top Scores", right.transform, "", bodyFont, 28, TextColor, TextAlignmentOptions.TopLeft, new Vector2(0.5f, 1f), new Vector2(0, -95), new Vector2(460, 440));

            ui.gameOverFirst = MakeButton("RETRY", t, c, new Vector2(-180, -380), new Vector2(320, 70), ui.Retry, 40);
            MakeButton("MAIN MENU", t, c, new Vector2(180, -380), new Vector2(320, 70), ui.MainMenu);
            return screen;
        }

        static ScreenFader BuildFader(Transform canvas)
        {
            RectTransform rt = Stretch("Fade", canvas);
            Image black = rt.gameObject.AddComponent<Image>();
            black.color = Color.black;
            black.raycastTarget = false;
            rt.gameObject.AddComponent<CanvasGroup>();
            return rt.gameObject.AddComponent<ScreenFader>();
        }

        // ==============================================================
        // Menu camera + scene wiring
        // ==============================================================
        static void BuildMenuCamera(Transform root)
        {
            GameObject go = new GameObject("CM Menu Camera");
            go.transform.SetParent(root, false);
            CinemachineCamera cam = go.AddComponent<CinemachineCamera>();
            LensSettings lens = LensSettings.Default;
            lens.FieldOfView = 50f;
            lens.FarClipPlane = 3000f;
            cam.Lens = lens;
            MenuCamera orbit = go.AddComponent<MenuCamera>();
            // The bay was turned so the boat sails out to sea (WorldLayout): turn the orbit with it.
            orbit.orbitCentre = WorldLayout.ToWorld(orbit.orbitCentre);
            orbit.lookAt = WorldLayout.ToWorld(orbit.lookAt);
            orbit.baseAngle += WorldLayout.Yaw;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            SaveAsset(profile, Generated + "/PP_MenuDepthOfField.asset");
            DepthOfField dof = profile.Add<DepthOfField>(true);
            dof.name = "DepthOfField";
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(70f);
            dof.gaussianEnd.Override(300f);
            dof.gaussianMaxRadius.Override(1f);
            AssetDatabase.AddObjectToAsset(dof, profile);
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.name = "Vignette";
            vignette.intensity.Override(0.25f);
            AssetDatabase.AddObjectToAsset(vignette, profile);
            EditorUtility.SetDirty(profile);

            Volume volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 15f;
            volume.weight = 1f;
            volume.sharedProfile = profile;
            orbit.menuVolume = volume;
        }

        static void WireUpScene(Canvas hud)
        {
            // The UI now controls when the game and intro start.
            GameManager game = Object.FindAnyObjectByType<GameManager>();
            game.autoStartGameplay = false;
            EditorUtility.SetDirty(game);
            IntroCinematic intro = Object.FindAnyObjectByType<IntroCinematic>();
            if (intro != null) { intro.playOnStart = false; EditorUtility.SetDirty(intro); }

            // Retire the temporary debug HUD.
            DebugHUD debug = Object.FindAnyObjectByType<DebugHUD>();
            if (debug != null) { debug.enabled = false; EditorUtility.SetDirty(debug); }

            // Photo mode hides the real HUD now.
            PhotoMode photo = Object.FindAnyObjectByType<PhotoMode>();
            if (photo != null) { photo.hideWhileActive = new Behaviour[] { hud }; EditorUtility.SetDirty(photo); }
        }

        // ==============================================================
        // Quality levels: Low / Medium / High, each with its own URP asset
        // ==============================================================
        public static void SetupQualityLevels()
        {
            const string folder = "Assets/_StarlitTides/Settings/URP";
            MakeFolder(folder);
            UniversalRenderPipelineAsset template = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (template == null) { Debug.LogWarning("[Starlit Tides] No URP asset found, quality levels skipped."); return; }
            string templatePath = AssetDatabase.GetAssetPath(template);

            string[] names = { "Low", "Medium", "High" };
            UniversalRenderPipelineAsset[] assets = new UniversalRenderPipelineAsset[3];
            for (int i = 0; i < 3; i++)
            {
                string path = folder + "/URP_" + names[i] + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path) == null && path != templatePath)
                    AssetDatabase.CopyAsset(templatePath, path);
                UniversalRenderPipelineAsset a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                a.supportsCameraDepthTexture = true;     // the ocean needs these two
                a.supportsCameraOpaqueTexture = true;
                a.supportsHDR = true;                    // bloom needs HDR
                // Sharpness first: native resolution on Medium/High, and Low upscales
                // with FSR (which sharpens) instead of plain bilinear blur.
                a.renderScale = i == 0 ? 0.85f : 1f;
                a.upscalingFilter = i == 0 ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
                a.fsrOverrideSharpness = true;
                a.fsrSharpness = 0.9f;
                a.msaaSampleCount = i == 0 ? 1 : i == 1 ? 2 : 4;   // Low uses SMAA on the camera instead; Medium 2x (Phase 8, Step 6), High 4x
                // 64-bit HDR stops colour banding in the dark night gradients (sky, deep water).
                a.hdrColorBufferPrecision = i == 2 ? HDRColorBufferPrecision._64Bits : HDRColorBufferPrecision._32Bits;
                a.mainLightShadowmapResolution = i == 0 ? 1024 : i == 1 ? 2048 : 4096;
                a.shadowDistance = i == 0 ? 40f : i == 1 ? 70f : 110f;
                a.shadowCascadeCount = i == 0 ? 1 : i == 1 ? 2 : 4;
                SerializedObject so = new SerializedObject(a);
                SerializedProperty soft = so.FindProperty("m_SoftShadowsSupported");
                if (soft != null) soft.boolValue = i > 0;
                SerializedProperty softQuality = so.FindProperty("m_SoftShadowQuality");
                if (softQuality != null) softQuality.intValue = i == 2 ? 3 : 2;   // High / Medium
                // Full-resolution copy of the scene for the ocean's refraction (no 2x blur).
                SerializedProperty downsample = so.FindProperty("m_OpaqueDownsampling");
                if (downsample != null) downsample.intValue = i == 0 ? 1 : 0;
                so.ApplyModifiedPropertiesWithoutUndo();
                AddSharpenPass(a);
                EditorUtility.SetDirty(a);
                assets[i] = a;
            }

            // Quality levels live in ProjectSettings/QualitySettings.asset; edit it like any serialized asset.
            Object qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            SerializedObject quality = new SerializedObject(qualityAsset);
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            while (levels.arraySize < 3) levels.InsertArrayElementAtIndex(levels.arraySize - 1);
            while (levels.arraySize > 3) levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
            for (int i = 0; i < 3; i++)
            {
                SerializedProperty level = levels.GetArrayElementAtIndex(i);
                level.FindPropertyRelative("name").stringValue = names[i];
                level.FindPropertyRelative("customRenderPipeline").objectReferenceValue = assets[i];
                SetIfExists(level, "vSyncCount", 1);
                SetIfExists(level, "antiAliasing", 0);
                SetIfExists(level, "anisotropicTextures", i == 0 ? 1 : 2);   // forced on for Medium/High
                SetIfExists(level, "particleRaycastBudget", i == 0 ? 64 : i == 1 ? 256 : 1024);
                SetIfExists(level, "realtimeReflectionProbes", 1);
                SetFloatIfExists(level, "lodBias", i == 0 ? 0.7f : i == 1 ? 1f : 1.5f);
                SerializedProperty excluded = level.FindPropertyRelative("excludedTargetPlatforms");
                if (excluded != null) excluded.ClearArray();
            }
            // High is the default: the game is built to look its best (Low/Medium stay in Settings).
            SerializedProperty defaults = quality.FindProperty("m_PerPlatformDefaultQuality");
            if (defaults != null)
                for (int i = 0; i < defaults.arraySize; i++)
                    defaults.GetArrayElementAtIndex(i).FindPropertyRelative("second").intValue = 2;
            quality.FindProperty("m_CurrentQuality").intValue = 2;   // High while editing
            quality.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        // Adds the contrast-adaptive sharpening pass (StarlitSharpen.shader) to the
        // renderer as a URP "Full Screen Pass" feature, after post-processing.
        // SettingsManager sets its strength per quality level.
        static void AddSharpenPass(UniversalRenderPipelineAsset asset)
        {
            // The atmosphere (fog, moon scattering, god rays) goes in BEFORE post-processing
            // so bloom and tonemapping treat it like real light; sharpening comes last.
            AddFullScreenPass(asset, "Starlit Atmosphere", "Hidden/StarlitTides/Atmosphere", "M_Atmosphere",
                FullScreenPassRendererFeature.InjectionPoint.BeforeRenderingPostProcessing, ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
            AddFullScreenPass(asset, "Starlit Sharpen", "Hidden/StarlitTides/Sharpen", "M_Sharpen",
                FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing, ScriptableRenderPassInput.Color);
        }

        static void AddFullScreenPass(UniversalRenderPipelineAsset asset, string featureName, string shaderName, string materialName,
                                      FullScreenPassRendererFeature.InjectionPoint injection, ScriptableRenderPassInput requirements)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) { Debug.LogWarning("[Starlit Tides] " + shaderName + " missing, " + featureName + " skipped."); return; }
            string matPath = Generated + "/" + materialName + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, matPath);
            }

            SerializedProperty list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            for (int r = 0; r < list.arraySize; r++)
            {
                ScriptableRendererData data = list.GetArrayElementAtIndex(r).objectReferenceValue as ScriptableRendererData;
                if (data == null) continue;
                bool exists = false;
                foreach (ScriptableRendererFeature f in data.rendererFeatures)
                    if (f != null && f.name == featureName) { exists = true; ((FullScreenPassRendererFeature)f).passMaterial = material; }
                if (exists) continue;

                FullScreenPassRendererFeature feature = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                feature.name = featureName;
                feature.passMaterial = material;
                feature.injectionPoint = injection;
                feature.fetchColorBuffer = true;
                feature.requirements = requirements;
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);

                SerializedObject dataSo = new SerializedObject(data);
                SerializedProperty map = dataSo.FindProperty("m_RendererFeatureMap");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string guid, out long localId);
                map.arraySize = data.rendererFeatures.Count;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                dataSo.ApplyModifiedPropertiesWithoutUndo();
                data.SetDirty();
                EditorUtility.SetDirty(data);
            }
        }

        static void SetIfExists(SerializedProperty parent, string name, int value)
        {
            SerializedProperty p = parent.FindPropertyRelative(name);
            if (p != null) p.intValue = value;
        }

        static void SetFloatIfExists(SerializedProperty parent, string name, float value)
        {
            SerializedProperty p = parent.FindPropertyRelative(name);
            if (p != null) p.floatValue = value;
        }
    }
}
#endif
