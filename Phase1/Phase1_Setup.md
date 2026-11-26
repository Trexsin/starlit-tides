# Starlit Tides — Phase 1: Project Setup

Goal for this phase: a clean Unity 6 URP project with every package installed, the folder structure in place, three quality presets (Low / Medium / High) set up, a single main scene, and the free assets downloaded and ready to import in Phase 2.

Time needed: about 45–60 minutes, most of it waiting for downloads.

---

## 0. What you already have

- **Unity 6000.5.10f1** (and 6000.5.9f1) is installed through Unity Hub. Use **6000.5.10f1** for everything. Don't open this project in 2022.3 later: the scripts in the next phases use Unity 6 APIs (Cinemachine 3, `Rigidbody.linearVelocity`, Build Profiles).
- **Missing: Windows Build Support (Mono).** You need it to build the `.exe` on your Mac. Install it now (step 1) so it's ready for Phase 6.
- This folder also contains an older C++/OpenGL version (`starlit_tides.cpp`). It is left untouched. The Unity project goes into its own subfolder.

---

## 1. Add the Windows build module

1. Open **Unity Hub → Installs**.
2. Find **Unity 6000.5.10f1**, click the **gear icon (⚙) → Add modules**.
3. Tick **Windows Build Support (Mono)** → **Install**.

> On a Mac you can only build Windows games with the **Mono** scripting backend (IL2CPP for Windows needs a Windows PC). Mono is fine for this game, so nothing is lost.

---

## 2. Create the project

1. **Unity Hub → Projects → New project**.
2. Editor version at the top: **6000.5.10f1**.
3. Template: **Universal 3D** (sometimes shown as "3D (URP)"). Download it if Hub asks.
4. Project name: **`StarlitTides`**
5. Location: **`/Users/trexsin/Downloads/CG project`**
6. Untick *Connect to Unity Cloud* / *Use Unity Version Control* (not needed).
7. **Create project**. The first open takes a few minutes.

You end up with `CG project/StarlitTides/` next to the C++ files.

**Clean up the template** (Project window):
- Delete `Assets/TutorialInfo` and `Assets/Readme.asset` (template tutorial files).
- Keep `Assets/Settings` for now. We reorganise it in step 6.

---

## 3. Run the setup script (folders + packages + player settings)

The file [`StarlitTidesSetup.cs`](StarlitTidesSetup.cs) (in this `Phase1` folder) adds a **Starlit Tides** menu to Unity. It compiles cleanly against your Unity 6000.5 install.

1. In Finder, create the folder `StarlitTides/Assets/_StarlitTides/Scripts/Editor/`.
2. Copy `StarlitTidesSetup.cs` into it.
3. Back in Unity, wait for the compile spinner (bottom-right) to finish. A new **Starlit Tides** menu appears in the top menu bar.
4. Click in order:
   - **Starlit Tides → 1. Create Folder Structure**
   - **Starlit Tides → 2. Install Packages** (watch the Console. It takes 2–5 minutes, then Unity recompiles.)
   - **Starlit Tides → 3. Apply Player Settings**

### What the folder structure looks like

```
Assets/
└── _StarlitTides/            ← everything of ours (underscore = sorted to the top)
    ├── Scenes/               StarlitBay (main), Sandbox_Ocean (shader testing)
    ├── Scripts/
    │   ├── Core/             GameManager, HighScoreManager, ObjectPool
    │   ├── Gameplay/         BoatController, LightMeter, Orb, Obstacle, Jellyfish, spawners
    │   ├── Features/         Extra features 1–6
    │   ├── Bonus/            Bonus features 7–11
    │   ├── Environment/      Lantern flicker, lighthouse, village life
    │   ├── CameraFX/         CameraShake, menu orbit
    │   ├── UI/  Audio/  Editor/
    ├── Materials/  (Environment, Gameplay, VFX)
    ├── Shaders/              Ocean, NightSky, Aurora, Glow Shader Graphs
    ├── Prefabs/    (Environment, Gameplay, VFX, UI)
    ├── VFX/Textures/
    ├── Audio/      (Music, SFX, Ambience, Mixers)
    ├── UI/         (Fonts, Sprites)
    ├── Models/     (Environment, Gameplay)
    ├── Textures/
    ├── Settings/   (URP, Volumes, Input)
    ├── Timeline/             intro cinematic
    └── ThirdParty/           imported free packs go here
```

### If the package install fails: install by hand

**Window → Package Manager → Unity Registry** tab, search each name and click **Install**. Or use **+ → Install package by name…** and paste the ID.

| Package | ID | Why we need it |
|---|---|---|
| Universal RP | `com.unity.render-pipelines.universal` | The render pipeline (already in the template) |
| Shader Graph | `com.unity.shadergraph` | Ocean, sky, aurora and glow shaders |
| Input System | `com.unity.inputsystem` | WASD / arrows / Space / Esc + gamepad |
| **Cinemachine 3.x** | `com.unity.cinemachine` | Follow cam, menu orbit, intro flythrough, camera shake |
| Timeline | `com.unity.timeline` | Day-to-night intro cinematic (Feature 6) |
| Splines | `com.unity.splines` | Camera paths for the flythrough |
| ProBuilder | `com.unity.probuilder` | Pier, bridge, market stalls, cliffs |
| uGUI (incl. TextMeshPro) | `com.unity.ugui` | Menus and HUD. In Unity 6, TextMeshPro lives inside uGUI. |
| Recorder (optional) | `com.unity.recorder` | Record a demo video for your presentation |

> ⚠️ **Cinemachine must be version 3.1.8 or newer.** Your editor also ships Cinemachine 2.10 offline, and **3.1.2 does not compile on Unity 6000.5** (error CS0619 in CinemachineStoryboard.cs). If Package Manager shows another version, open the package's **Version History** tab and install **3.1.8**. Do **not** install the old *Post Processing* (v2) package either: URP has post-processing built in through **Volumes**.

### After the packages finish

1. **Window → TextMeshPro → Import TMP Essential Resources** → **Import**.
2. **Edit → Project Settings → Player → Other Settings → Configuration → Active Input Handling** must be **Input System Package (New)** (or **Both**). If Unity asks to restart, click **Yes**.
3. Same page, **Rendering → Color Space** must be **Linear** (the script sets this; just check it).

---

## 4. Main scene

We use **one main scene** for the menu, the intro cinematic and gameplay:

- The animated main menu is a camera orbiting the real glowing bay, so the menu needs the full environment anyway.
- Menu → intro → gameplay become smooth camera blends with no loading screens.
- One scene means one lighting bake and one set of probes to maintain.

Steps:
1. Rename `Assets/Scenes/SampleScene` to **`StarlitBay`** and move it into `_StarlitTides/Scenes/`. Delete the now-empty `Assets/Scenes` folder.
2. **File → New Scene → Basic (URP)** → save as `_StarlitTides/Scenes/Sandbox_Ocean`. This is a scratch scene for testing the ocean shader in Phase 2.
3. **File → Build Profiles → Scene List**: remove `SampleScene` and add `StarlitBay` (drag it in).
4. Move the template's `Assets/InputSystem_Actions.inputactions` into `_StarlitTides/Settings/Input/`. In Phase 3 we make our own `StarlitControls` actions.

---

## 5. Game view setup (so it looks right while you build)

- **Game view → aspect dropdown → +** → *Fixed Resolution* **1920 × 1080**, label "Full HD". Select it.
- In the **Scene view**, open the effects toggle (the picture icon in the scene toolbar) and enable **Always Refresh**, **Fog**, **Skybox** and **Post Processing**. Then the Scene view shows bloom and fog while you decorate.

---

## 6. Rendering setup: URP quality presets

The water shader **requires** the Depth Texture and Opaque Texture, and Bloom glows properly only with **HDR**. Set these on every preset.

### 6a. Organise the template assets

1. Move everything from `Assets/Settings` into `_StarlitTides/Settings/URP/`. Moving keeps all references intact.
2. Rename `PC_Renderer` → **`URP_Renderer`** and `PC_RPAsset` → **`URP_High`**.
3. Select `URP_High` and press **Cmd+D** twice. Rename the copies **`URP_Medium`** and **`URP_Low`**.
4. Delete `Mobile_RPAsset` and `Mobile_Renderer` (we are PC only). If Unity warns they are in use, do this *after* step 6d.
5. Move `DefaultVolumeProfile` and `SampleSceneProfile` to `Settings/Volumes/`. In Phase 2 we replace them with our own night profile.

### 6b. Renderer: Forward+

Select **`URP_Renderer`** in the Inspector:
- **Rendering → Rendering Path: `Forward+`**
  *Why:* the village has many small lights (windows, lanterns, market stalls). Classic Forward is limited to 8 lights per object, so lanterns would pop on and off. Forward+ removes that limit.
- **Depth Priming Mode:** Disabled
- **Post-processing → Enabled:** ✔

### 6c. Pipeline asset values

Select each asset. Turn on **⋮ (top-right of the Inspector) → Show Additional Properties** to see every field.

| Setting (Inspector section) | URP_Low | URP_Medium | URP_High |
|---|---|---|---|
| **Rendering → Depth Texture** | ✔ | ✔ | ✔ |
| **Rendering → Opaque Texture** | ✔ | ✔ | ✔ |
| Rendering → Opaque Downsampling | 4x Box | 2x Bilinear | None |
| **Quality → HDR** | ✔ | ✔ | ✔ |
| Quality → HDR Precision | 32 Bits | 32 Bits | 64 Bits |
| Quality → Anti Aliasing (MSAA) | Disabled | 2x | 4x |
| Quality → Render Scale | 0.8 | 1.0 | 1.0 |
| Quality → Upscaling Filter | Automatic | Automatic | Automatic |
| Lighting → Main Light / Cast Shadows | Per Pixel / ✔ | Per Pixel / ✔ | Per Pixel / ✔ |
| Lighting → Main Light Shadow Resolution | 512 | 1024 | 2048 |
| Lighting → Additional Lights | Per Pixel | Per Pixel | Per Pixel |
| Lighting → Additional Lights Cast Shadows | ✘ | ✘ | ✔ (atlas 2048, low tier 256) |
| Lighting → Reflection Probes: Probe Blending | ✘ | ✔ | ✔ |
| Lighting → Reflection Probes: Box Projection | ✘ | ✘ | ✔ |
| Shadows → Max Distance | 40 | 70 | 100 |
| Shadows → Cascade Count | 1 | 2 | 4 |
| Shadows → Soft Shadows | ✘ | ✔ (Low) | ✔ (Medium) |
| Post-processing → Grading Mode | High Dynamic Range | High Dynamic Range | High Dynamic Range |
| Post-processing → LUT Size | 16 | 32 | 32 |
| Advanced → SRP Batcher | ✔ | ✔ | ✔ |
| Advanced → Dynamic Batching | ✘ | ✘ | ✘ |

All three use **`URP_Renderer`** in their **Renderer List**.

### 6d. Quality levels

**Edit → Project Settings → Quality**:
1. Click the template level **PC** and rename it **High**. Assign **Render Pipeline Asset = URP_High**.
2. **Add Quality Level** twice → name them **Low** and **Medium**, assign `URP_Low` / `URP_Medium`.
3. Delete the **Mobile** level (trash icon).
4. In the matrix at the top, set the **Windows/Standalone default** (the green tick under the PC icon) to **Medium**. That is a safe default for mid-range laptops. The Settings menu (Phase 6) lets players switch, and you can choose High on the demo machine.
5. Per level:

| Quality level field | Low | Medium | High |
|---|---|---|---|
| VSync Count | Every V Blank | Every V Blank | Every V Blank |
| Global Mipmap Limit (texture quality) | Half Resolution | Full Resolution | Full Resolution |
| Anisotropic Textures | Disabled | Per Texture | Forced On |
| LOD Bias | 0.7 | 1.0 | 1.5 |
| Particle Raycast Budget | 64 | 256 | 1024 |
| Realtime Reflection Probes | ✘ | ✔ | ✔ |

6. **Edit → Project Settings → Graphics → Default Render Pipeline = URP_High**. This is used whenever a quality level has no asset, and while editing.

---

## 7. How the game world works (design decision)

The brief asks us to choose between *a fixed play area with a scrolling world* and *moving forward on recycled ocean chunks*. **We use the fixed play area, with the tide flowing toward the boat.**

**How it works:** the boat stays in a bounded "play lane" near the world origin. It moves left/right and a little forward/back within that lane, and the camera barely moves. The **sea scrolls** (wave, foam and plankton textures scroll in the shader) and orbs, logs, rocks and jellyfish spawn far ahead and **drift toward the boat** on the current. The village, pier, bridge, lighthouse and mountains **never move**.

**Why this is the most stable choice:**
1. **The environment stays in shot.** The village, bridge, forest and mountains are the course requirements, and with a static world they stay visible behind the action for the whole run. With chunks, the coast would scroll away or need endless copies.
2. **No floating-point drift.** A boat travelling forward for 10 minutes reaches large coordinates, where physics and shaders start to jitter. Ours never leaves the origin.
3. **Lighting is bakeable.** Static scenery means baked lights, light probes and one reflection probe on the water all work. Recycled chunks would break baking.
4. **Simple pooling.** Objects spawn at one line, drift, and return to the pool when they pass behind the camera. No chunk seams and no loading spikes.
5. **It fits the theme.** "Starlit *Tides*": the tide carries the plankton and the debris toward you.

### World layout (top-down)

```
                       N  (+Z, far away: mountains fade into fog)
        ▲▲▲▲▲  ▲▲▲▲▲▲▲  ▲▲▲▲   ▲▲▲▲▲▲   ← layered mountains (Z ≈ 400–900)
     🌲🌲🌲🌲🌲🌲 forest on hills 🌲🌲🌲      ☼ moon above the sea
   🌲🌲   grassland   🌲    🗼 lighthouse on cliff (X≈-40, Z≈160)
  🌲   ~~~lagoon~~~ ═╗                     .  spawn line (Z ≈ 120)
  🌲  ~~~~~~~~~~~~ ⌒bridge⌒ ~~channel~~~~~ .  orbs / logs / rocks / jellyfish
      market⛺⛺  ║                         .   drift toward -Z on the tide
   🏠🏠 [ village square ] 🏠   ┃ pier       .
   🏠  lantern street   🏠🏠    ┃━━━━━   |  PLAY LANE  |  open sea →
   🏠🏠  beach / foam line ~~~~~~~~~~~~  |  X = -8..8  |
                                        |     ⛵ boat   |   (Z ≈ 0)
                                        |   camera ↘   |
                       S  (-Z: objects despawn behind the camera, Z ≈ -20)
```

- Shore and village on the **left** (−X), open sea on the **right** (+X). The camera sits behind and above the boat and looks slightly left-forward, so the glowing village fills the left of the frame and the mountains and moon fill the horizon.
- The **pier** points from the village toward the lane but stops short of it.
- The **lagoon** sits behind the village and joins the sea through a channel under the **arched bridge**.
- The **lighthouse** beam sweeps across the play lane (Bonus 8 reveals hidden orbs).

We build this exactly in Phase 2.

---

## 8. Free assets to download now

All CC0 (free for any use, no credit required, though crediting is polite) unless noted. Download the packs and **unzip them outside the project for now**. In Phase 2 I'll tell you exactly which models to import, so the project doesn't fill with hundreds of unused files.

### 3D models

| Pack | Link | License | Use it for |
|---|---|---|---|
| **Quaternius – Medieval Village MegaKit** | https://quaternius.com/packs/medievalvillagemegakit.html | CC0 (free portion) | Modular houses, roofs, doors, windows → the 8–12 village houses. Only part of the kit is free; the free part is enough. |
| **Quaternius – Stylized Nature MegaKit** | https://quaternius.com/packs/stylizednaturemegakit.html | CC0 (free portion) | Stylized trees, rocks, bushes, grass, flowers → forest and grassland |
| **Quaternius – Ultimate Nature Pack** | https://quaternius.com/packs/ultimatenature.html | CC0 | 150 nature models: more tree types and rocks → the forest along the hills. Look for pine and palm variants. |
| Quaternius – Simple Buildings Pack | https://quaternius.com/packs/simplebuildings.html | CC0 | Backup houses if you want more variety |
| **Kenney – Fantasy Town Kit** | https://kenney.nl/assets/fantasy-town-kit | CC0 | 160 medieval town pieces (buildings, walls, props) for the village square. Market stalls are built in ProBuilder if the kit has none. |
| **Kenney – Nature Kit** | https://kenney.nl/assets/nature-kit | CC0 | 330 trees, rocks and foliage → rocks (obstacles), logs, grass clumps |
| **Kenney – Pirate Kit** | https://kenney.nl/assets/pirate-kit | CC0 | Tropical island pieces and ships → props for the pier and beach, sea rocks |
| Kenney – Watercraft Kit | https://kenney.nl/assets/watercraft-kit | CC0 | Fishing boats moored in the bay (decoration) |
| **Poly Pizza – Whale** (Quaternius) | https://poly.pizza/m/JGFwp6xWgk | Check model page (Quaternius models are normally CC0) | The glowing whale event (Bonus 7) |
| Poly Pizza – Lighthouse options | https://poly.pizza/search/lighthouse | **Check each model page** (CC0 or CC-BY) | Lighthouse. Otherwise we build one in ProBuilder. |
| Poly Pizza – Lantern / Post Lantern (Kay Lousberg) | https://poly.pizza/m/CtHBJ1ufeW · https://poly.pizza/m/ZSQ65S4lEu | Check model page | Street lanterns and stall lanterns |

> **Poly Pizza:** licenses vary per model. CC0 = free, nothing required. **CC-BY = free, but you must credit the author** (we add a Credits section to the How to Play screen). Check the license line on each model page before downloading.

### Asset Store (free, open with "Add to My Assets", then import from Package Manager → My Assets)

| Pack | Link | Use it for |
|---|---|---|
| **Cartoon FX Remaster Free** (Jean Moreno) | https://assetstore.unity.com/packages/vfx/particles/cartoon-fx-remaster-free-109565 | Splash, sparkle and hit bursts to modify into our cyan glow VFX |
| Fantasy Skybox FREE (Render Knight) | https://assetstore.unity.com/packages/2d/textures-materials/sky/fantasy-skybox-free-18353 | **Backup only.** We build our own procedural night sky shader (Milky Way, twinkling stars, moon, aurora), which looks better and supports the weather changes. |

### Textures and VFX

| Pack | Link | Use it for |
|---|---|---|
| **Kenney – Particle Pack** | https://kenney.nl/assets/particle-pack | Soft circles, sparkles, stars, smoke → plankton, fireflies, rain, splashes |
| Poly Haven textures | https://polyhaven.com/textures | Optional sand, rock or wood detail textures (CC0) |

### Fonts (Google Fonts, SIL Open Font License, free for games)

| Font | Link | Use |
|---|---|---|
| **Cinzel** | https://fonts.google.com/specimen/Cinzel | Title "STARLIT TIDES", headings: elegant, classical serif |
| **Cormorant Garamond** | https://fonts.google.com/specimen/Cormorant+Garamond | Subtitles, "The tide has gone dark…" |
| **Quicksand** | https://fonts.google.com/specimen/Quicksand | HUD numbers (score, combo): clean and readable |

Click **Get font → Download all**. Copy the `.ttf` files into `_StarlitTides/UI/Fonts/` now. We convert them to glowing TextMeshPro font assets in Phase 6.

### Audio (collect a few now, the rest in Phase 6)

| Source | Link | Notes |
|---|---|---|
| **Kenney – Interface Sounds** | https://kenney.nl/assets/interface-sounds | Menu clicks and hovers (CC0) |
| **Kenney – Impact Sounds** | https://kenney.nl/assets/impact-sounds | Hitting rocks and logs (CC0) |
| Pixabay Music | https://pixabay.com/music/ | Search "ambient night ocean", "calm piano ambient", "epic ambient" (Pixabay Content License, free, no credit needed) |
| Pixabay Sound Effects | https://pixabay.com/sound-effects/ | Waves, wind, rain, thunder, whale |
| Freesound | https://freesound.org | Use the **License filter → Creative Commons 0** to avoid credit requirements |

We need, eventually: **2 music tracks** (calm layer and intense layer with the same tempo/key if possible, for adaptive mixing), **loops** for waves, wind and rain, and **one-shots** for thunder, orb pickup (bright chime), dash (whoosh), hit (wooden thud), near-miss (shimmer), whale call, starburst (magical swell) and UI click.

### What we build ourselves (no assets needed)

These are where the Computer Graphics marks are, so they are custom:
- **Ocean shader** (Shader Graph): vertex waves, depth colour, shore foam, bioluminescent crests
- **Night sky shader**: Milky Way, twinkling stars, moon with halo, plus the **aurora** shader
- **Paper boat**: a procedural folded-paper mesh made by a script, with an emissive glow
- **Plankton orbs, jellyfish** (pulsing, vertex-animated bells) and the **lighthouse light cone**
- **Pier and bridge** in ProBuilder (exact dimensions in Phase 2)

---

## 9. Optional but strongly recommended: version control

A course project can get damaged by one bad save. In Terminal:

```bash
cd "/Users/trexsin/Downloads/CG project/StarlitTides"
```

```bash
curl -sL https://raw.githubusercontent.com/github/gitignore/main/Unity.gitignore -o .gitignore && git init && git add . && git commit -m "Phase 1: project setup"
```

Commit after each phase. Rolling back a broken shader then takes seconds.

---

## 10. Tips that make it look better from day one

- **Linear + HDR + Forward+** together are the foundation for bloom. Without HDR, emissive colours clip at white and the neon glow looks flat.
- **Don't light the scene yet.** In Phase 2 we remove the default bright directional light and blue skybox first, then build the night look from darkness up. Night scenes look best when you *add* light, not when you darken a day scene.
- **Keep imported models in `ThirdParty/`** and make **prefabs** of the pieces you actually use in `Prefabs/Environment`. Re-skinning or swapping a model later then means editing one prefab.
- **Use one shared palette.** We will use deep navy `#0B1026`, purple `#2A1B4D`, teal `#0E4D5C`, neon cyan `#3FF7FF` and soft gold `#FFC873`. Write these down. Every material and light in Phase 2 picks from them, which is what makes a scene look "designed" rather than assembled.

---

## ✅ Phase 1 checklist

- [ ] Windows Build Support (Mono) module installed
- [ ] `StarlitTides` project created from the **Universal 3D** template inside `CG project/`
- [ ] Template tutorial files deleted
- [ ] **Starlit Tides** menu commands 1, 2 and 3 run, with no red errors in the Console
- [ ] **Cinemachine 3.1.8** confirmed in Package Manager (not 2.10 or 3.1.2)
- [ ] TMP Essential Resources imported; Active Input Handling = Input System (or Both); Color Space = Linear
- [ ] `StarlitBay` and `Sandbox_Ocean` scenes created; `StarlitBay` in the Build Profiles scene list
- [ ] `URP_Low`, `URP_Medium`, `URP_High` created with the values above; renderer on **Forward+**
- [ ] Quality levels Low / Medium / High assigned; Windows default = Medium
- [ ] Assets and fonts downloaded (unzipped outside the project, fonts copied into `UI/Fonts`)
- [ ] (Optional) git initialised and first commit made

When you've finished this, tell me and we'll start **Phase 2: the environment**: the ocean Shader Graph (node by node), the night sky, then the village, pier, bridge, lagoon, forest, mountains, lighting and post-processing.
