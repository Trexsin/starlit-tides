# Starlit Tides — Phase 8 Plan: Stylized Look + Wider Play Area

**Status:** All six steps are done; see [`Phase8_Stylized_Overhaul.md`](Phase8_Stylized_Overhaul.md) for the overview and the step guides. Left over from Step 3: a stylized terrain material, creature outlines and the pier.
**Feedback this plan answers:**
1. *"The graphics are too low poly and look bad. Make them stylized, fitting the theme, and beautiful."*
2. *"The play area is too narrow. The camera shows a wide view, so only a small part of the map has objects in it."*

---

## 0. What is wrong today (measured from the code and the Phase 7 renders)

### 0.1 The empty-screen problem

| Setting | Value now | Where |
|---|---|---|
| Boat lane | x = **±13 m** | `BoatController.laneX` |
| Obstacle lane | x = ±14 m | `ObstacleSpawner.laneX` |
| Orb lane | x = ±12.5 m (hidden orbs ±12 m) | `OrbSpawner.laneX`, `HiddenOrbs.laneX` |
| Spawn distance | 110–112 m ahead | spawners |
| Camera | 8 m up, 16 m back, **58° vertical FOV**, looks 26 m ahead | `GameplayBuilder` (CM Gameplay Camera) |

At 16:9 a 58° vertical FOV is about **89° horizontal**. Where objects spawn (~128 m from the camera) the screen is about **250 m wide**, but the lane is only **28 m wide**. So everything spawns in a thin strip in the middle (about 11% of the screen width) and drifts into a funnel. The left and right thirds of the screen are only empty water. The camera is also low (about 10° pitch), so a third of the screen is sky and the far sea is squashed.

### 0.2 The "low poly" problem

| Asset | Why it reads as low poly |
|---|---|
| Mountains (`ProcMesh.MountainRidge`) | only 2 rows of quads, so every peak is one flat triangle |
| Pine trees | 4 cones with 7 sides each, plus a 6-sided trunk, flat-shaded |
| Round trees | icosphere detail 1 (80 faces per blob) |
| Houses / stalls | plain boxes with no bevels, overhangs or trim |
| Lighthouse cliff, terrain | stock URP Lit with flat colours and hard grey shading |
| Moon | a flat white disc with no detail or halo |
| Most props | `Universal Render Pipeline/Lit` with one flat colour: realistic shading on toy shapes looks cheap |

The fix is not only "more polygons". A **consistent stylized shading model** makes simple shapes look intentional. Smooth silhouettes still need more geometry where the outline shows (mountains, trees, lighthouse, moon).

---

## 1. Art direction: "Moonlit Storybook"

A painterly, hand-made night: soft shapes, gradients rather than textures, glowing accents. It should feel like a children's-book illustration of a bioluminescent bay.

**Palette** (all materials and shaders draw from this):

| Role | Colour | Use |
|---|---|---|
| Deep night | `#0E1030` indigo | shadow tint, far sky, deep water |
| Mid sea | `#1D4E6B` → `#2A7F8F` teal | water body and its gradient |
| Bioluminescence | `#5FF5E6` cyan, `#9BFFC8` mint | orbs, plankton, trails, lichen |
| Moonlight | `#CFE0FF` pale blue | key light, rim light, highlights |
| Warm accent | `#FFB45E` amber, `#FF7A59` coral | lanterns, windows, boat candle |
| Magic accent | `#C38BFF` lilac, `#FF8BD1` pink | jellyfish, aurora, creature patterns |

**Rules:**
- **Shadows are coloured, never black or grey.** They are indigo and violet.
- **Every object has a moonlit rim** on its moon-facing silhouette, which separates it from the dark water.
- **Soft, rounded silhouettes** (bevels, smooth normals) on everything except the origami boat, which stays faceted on purpose.
- **Warm against cool:** the warm lights are small and rare, so they read as precious against the cool world.
- **Depth through atmosphere:** every distance layer is lighter, bluer and less saturated (aerial perspective), with low mist on the water.
- **Glow means gameplay.** Cyan or mint glow marks things to collect, and coral or pink marks danger. Decorative fill uses lower-intensity versions so it never gets confused with gameplay objects.

---

## 2. Workstream A: play area and camera (do this first; biggest win for the least work)

### A1. Widen the lane (×1.7)

| Field | Now | New |
|---|---|---|
| `BoatController.laneX` | ±13 | **±22** |
| `ObstacleSpawner.laneX` | ±14 | **±24** |
| `OrbSpawner.laneX` | ±12.5 | **±21.5** |
| `HiddenOrbs.laneX` | ±12 | **±21** |
| Creature spawn band (`ObstacleSpawner`, line 67) | 0.45–1.0 × lane | the same, plus creatures that start **off-lane** (x = ±40) and swim in |

- **Keep the difficulty the same.** Scale the obstacles and orbs spawned per wave by the width ratio (×1.7) so the density per square metre stays the same. Add a `laneWidthScale` factor to `DifficultyManager` and let the spawners multiply their counts by it.
- **The gap guarantee** (`ObstacleSpawner` "largest free gap") already works in lane coordinates. Its edge maths uses `laneX`, so it adapts. Re-test that `minimumGap` (3.2 m) still always leaves a route open.
- **Boat speed:** raise the lateral max speed by about 25% so crossing the wider lane still feels snappy (crossing time ~2.2 s instead of ~1.5 s).
- **One source of truth:** a `PlayArea` component on Managers (`laneHalfWidth`, `obstacleOverhang`, `orbInset`, `edgeMarkerX`, `laneZ`). The boat, the spawners, the markers and the ocean shader all read it. *(Built as a scene component rather than a ScriptableObject: no asset to wire up, and it has defaults when it's missing.)*

### A2. Re-frame the camera

| Setting | Now | New |
|---|---|---|
| Follow offset | (0.5, 8, −16) | **(0, 11, −19)** |
| Look-at point | boat + (−1.5, 0.5, 26) | **anchor + (0, 1, 34)** |
| Vertical FOV | 58° | **48°** (≈ 77° horizontal at 16:9) |
| Pitch | ~10° | **~11°** (a steeper pitch pushed the moon off-screen) |
| X tracking | follows the boat 100% | follows the boat at **55%** (a new `CameraAnchor` target) |

Result: at spawn distance the visible width drops from about 250 m to about 205 m, and the lane plus its buoys fill about **27%** of it instead of 11%. The moon was lowered from 12° to 8° so it stays in frame, and the land was slid 18 m left (`WorldLayout.Offset`) so the beach no longer touches the wider lane. Because X tracking is partial, the boat visibly moves across the screen, so the lane edges feel like a place rather than an invisible wall.

- Add **ultrawide / aspect handling:** a tiny `CameraAspectFit` script that keeps the *horizontal* FOV roughly constant across 16:9, 16:10 and 21:9, so ultrawide players don't see even more empty sea.
- Keep the Cinemachine damping values. Re-tune `CameraDrift` (Feature) amplitudes for the new distance.
- **Dash:** add a small FOV kick (+4°) as a speed cue.

### A3. Make the lane edges visible (soft and diegetic)

- **A reef line:** glowing buoys and lichen reefs every 16 m along x = ±27 that scroll with the tide. The player reads them as the edge of the safe channel.
- **Water colour:** the ocean shader takes a `_LaneHalfWidth` uniform and makes the water slightly darker and more indigo past the lane (subtle, about 15%). The plankton glow is strongest inside the lane.
- `BoatController` already has a soft edge clamp. Add a light **bump-back ripple + sound** when the boat touches the edge.

### A4. Fill the world beyond the lane (`AmbientSeaField`, new script + builder)

A pooled, **non-colliding** decoration spawner that scrolls with the tide like the obstacles but covers **|x| from 27 m out to 140 m**. These are cheap meshes with no physics, so the whole screen reads as a living sea.

| Band | Distance from lane | Content |
|---|---|---|
| Near flank | 27–50 m | reef/buoy line, bioluminescent kelp beds (emissive patches under the water surface), floating paper lanterns, small rock clusters with glowing lichen |
| Mid flank | 50–90 m | sea stacks and natural rock arches, a little fishing boat with a lamp, groups of resting turtles, lily-pad-like glowing algae rafts |
| Far flank | 90–140 m | larger sea stacks with trees on top, whale spouts (particles), leaping dolphin pods (reuse `SeaCreature` with collision off), drifting fog banks |
| Horizon (static, does not scroll) | 400–900 m | silhouetted islands, a distant second lighthouse, faint ship lights, low mist band |

- The density rises toward the screen edges, so the eye is led back to the centre.
- **Event moments:** about every 45 s, one "set piece" scrolls past on the flank: a giant glowing whale (reuse `AmbientWhale`), a ring of floating lanterns, or a coral arch you can see through.
- Pools are budgeted: about 60 active decorations, GPU-instanced, with LOD and shadows off past 60 m.

---

## 3. Workstream B: the stylized shading model (a CG-course centrepiece)

### B1. `StarlitStylized.shader` (hand-written URP HLSL, like the ocean)

One lit shader for **every opaque prop** (houses, rocks, trees, lighthouse, logs, terrain props, creatures). It replaces `Universal Render Pipeline/Lit` in the builders.

| Term | Technique |
|---|---|
| Diffuse | `N·L` remapped through a **3-band ramp** (smoothstep-softened bands, `_BandSoftness`), so you get soft toon shading rather than hard cel shading |
| Shadow colour | a lerp toward `_ShadowTint` (indigo), not toward black. URP main-light shadows are sampled and folded into the same ramp |
| Ambient | a **hemisphere light**: sky indigo from above and teal sea bounce from below (`lerp(_GroundAmb, _SkyAmb, N.y*0.5+0.5)`) |
| Rim | fresnel `pow(1 − N·V, _RimPower)` × moon direction mask, in the moonlight colour |
| Specular | stylized: one hard-edged highlight blob (`step(threshold, N·H)`), only on wet or glossy materials (rocks near water, creatures) |
| Gradient tint | an object-space **height gradient** (darker base, lighter top), which gives a "painted" look without textures |
| Paint noise | **triplanar** low-frequency noise (from `StarlitNoise.hlsl`) breaks up flat colour into brush-like variation |
| Additional lights | lanterns and the boat candle use the same banded ramp, with a warm falloff |
| Emission | a mask channel for windows, lichen and creature patterns, with optional pulse |
| Fog | URP fog plus the atmosphere pass (B4) |

Variants: `_STYLIZED_FOLIAGE` (spherical normals + wind), `_WET` (spec blob + darkening near the waterline), `_EMISSIVE_PATTERN`. Supports GPU instancing and the SRP Batcher.

### B2. Outlines

- **Inverted-hull outline pass** in the same shader (`Cull Front`, extruded along smoothed normals that are baked into UV3 by the mesh builders). It draws thin indigo lines, not black, and their width fades with distance.
- Used only on **hero objects**: the boat, creatures, the lighthouse, houses and obstacles. It is off on terrain and foliage. It is controlled per quality tier (off on Low).

### B3. Stylized foliage

- **Pine trees:** keep the stacked-tier shape, but make each tier a 16-sided cone with a jittered, drooping rim, and **smooth "spherical" normals** pointing out from the tree's centre axis. This is the classic trick that turns faceted cones into soft painted blobs.
- **Round trees:** icosphere detail **3** blobs, noise-displaced, with normals taken from the blob centre.
- **Wind:** vertex sway that increases with height (the same idea as the existing foliage Shader Graph lesson, moved into the stylized shader variant).
- **Grass:** a gradient from dark root to moonlit tip, plus a rim term.

### B4. Atmosphere and post

- **Height fog plus distance fog**, extending the existing `StarlitAtmosphere` pass. Low mist sits on the water (strongest at the horizon and around the coast), and colour shifts toward indigo with distance (aerial perspective).
- **Colour grading:** a custom LUT built in code. Shadows lean teal-indigo, highlights lean warm, and saturation is pushed slightly in the mid-tones.
- **Moon god rays:** a screen-space radial blur from the moon's screen position, masked by depth (Medium/High only).
- **Bloom:** keep the Phase 7 threshold (1.0) and add a lens-dirt-free soft halo only on emissive > 2.
- **Vignette:** soft, 0.25. **Depth of field:** menu, intro and photo mode only.

---

## 4. Workstream C: geometry upgrades (smooth where the silhouette shows)

| Asset | Change |
|---|---|
| **Mountains** | replace the 2-row ribbon with a **heightfield strip** (≈ 240 × 40 grid per layer): ridged noise + a cheap thermal-erosion smoothing pass, smooth normals. The shader colours it by height and slope: moonlit snow caps with a rim, dark forested lower slopes, a mist band at the base. Three layers, each lighter and bluer |
| **Terrain** | a stylized terrain material: slope-based grass or cliff blend, sand with a wet dark band at the shoreline, banded lighting. Cliff faces get vertical "brush stroke" noise |
| **Houses** | chamfered (bevelled) boxes, roof overhangs with a thickness, a ridge cap, chimneys with smoke particles, window frames and shutters, and a **warm light-spill decal** on the ground under each window |
| **Market stalls, crates** | bevelled edges, cloth awnings with a gentle wind wave |
| **Lighthouse** | a lathe profile with **32 segments** (smooth taper, gallery, railing posts, lantern room), stripes done in the shader rather than as separate geometry, and a glow halo at the lamp |
| **Pier and bridge** | planks with small random gaps and tilts, rope railings (catenary curves), posts with barnacle-like lichen glow at the waterline |
| **Rocks / obstacles** | keep the 2562-vertex icosphere, but **quantize the displacement** into soft planes (a "chunky stylized rock" look), add a moss/lichen gradient on top and cyan **bioluminescent lichen dots** (emissive pattern) so obstacles stand out from the water |
| **Drift logs** | 16-sided, with bark ridges in the normals, cut-face rings in the shader, a moss strip |
| **Jellyfish** | a smoother bell (32 × 12), fresnel transparency, an inner glowing core, ribbon tentacles that move with a sine wave down their length |
| **Paper boat** | **stays faceted** (origami). It gets a paper-fibre texture, crisp fold-crease lines in the shader, a **translucent glow** from the candle shining through the paper (wrap lighting + thickness), and a flickering candle-flame billboard |
| **Sea creatures** | already detailed. Move them to `StarlitStylized` with `_EMISSIVE_PATTERN` (glowing spots and lines along the body) and outlines |
| **Moon** | a sphere with crater noise and limb darkening, plus a big soft **halo** sprite and a ring corona. Its reflection path on the ocean gets wider and sparkles more |
| **Stars / sky** | three sizes of star with colour variety (blue-white to warm), twinkle, a faint **Milky Way band**, and painterly clouds (stepped-noise edges, moon-side rim lighting) |

Every builder that saves meshes also bakes **smoothed normals into UV3** for the outline pass.

---

## 5. Workstream D: ocean (the biggest surface on screen)

- **Stylized colour ramp by depth and view angle:** cyan-teal in the troughs near the camera, deep indigo far away, with no grey reflection wash.
- **Stylized foam:** crest foam as soft-edged **bands** (a stepped noise threshold), not noisy white pixels. Foam lines also form around rocks and creatures (using the depth difference), plus a foam wake behind the boat.
- **Bioluminescent trails:** the boat, creatures and orbs leave glowing cyan swirls that fade over about 3 s. This extends the existing `WaterDisturber` ripple texture with a glow channel.
- **Glowing plankton patches:** large, slowly drifting areas of soft glow across the whole visible sea (world-space noise), so the flanks are never flat.
- **Reduce the "noisy" look:** the Phase 7 micro-ripples make the water busy (it looks pixel-noisy in the screenshots). Lower their strength by about 50% and quantize the specular glints into fewer, larger, star-like sparkles.
- **Lane-edge tint:** see A3.

---

## 6. Order of work, with checkpoints

Each step ends with batch-mode renders from **fixed reference shots** (gameplay at 0 s and 30 s, a creature close-up, the menu, the village close-up) saved to `Phase8/Preview/`, so before and after can be compared side by side.

| Step | Work | Checkpoint |
|---|---|---|
| **0** | Commit the uncommitted Phase 7 work first, as a clean baseline | `git status` clean |
| **1** ✅ | Workstream A1–A3: `PlayArea`, wider lane, density scaling, new camera, lane edges | the lane fills ~25% of the screen at spawn distance; bot playtest with no unavoidable walls |
| **2** ✅ | Workstream B1–B2: `StarlitStylized` shader + outlines, switch all builders to it | the same scene now reads as toon-soft with coloured shadows and rim light |
| **3** ✅ | Workstream C: mountains, trees, houses, lighthouse, rocks, boat, moon | close-up renders: no visible facets on silhouettes except the boat |
| **4** ✅ | Workstream A4: `AmbientSeaField` decorations + horizon islands | the gameplay shot has interesting content across the full width |
| **5** ✅ | Workstream D + B4: ocean restyle, atmosphere, grading, god rays | final look pass |
| **6** ✅ | Performance and quality tiers (below), then update the README and the Phase 8 guide | Low ≥ 60 fps on integrated GPU, High ≥ 60 fps at 1440p |

Steps 1 and 2 are independent and could be done in either order. Step 1 first is recommended because it changes what the camera sees, which affects every visual decision after it.

---

## 7. Performance budget and quality tiers

| Feature | Low | Medium | High |
|---|---|---|---|
| Stylized shader bands + rim | ✓ | ✓ | ✓ |
| Outlines | off | hero objects | hero objects |
| Ambient decorations active | 25 | 45 | 60 |
| Horizon islands | silhouette cards | meshes | meshes |
| Bioluminescent trails | off | ✓ | ✓ |
| God rays | off | off | ✓ |
| Mountain grid | 120 × 20 | 240 × 40 | 240 × 40 |
| Foliage wind | off | ✓ | ✓ |

Rules: decorations past 60 m don't cast shadows, everything is GPU-instanced, and the SRP Batcher stays compatible (all per-material properties in `UnityPerMaterial`).

---

## 8. Files this will touch

**New**
- `Shaders/StarlitStylized.shader` (+ `StarlitStylizedCommon.hlsl`)
- ✅ `Scripts/Core/PlayArea.cs`, `Scripts/Features/CameraAspectFit.cs`, `Scripts/Features/CameraAnchor.cs`, `Scripts/Gameplay/LaneMarker(s).cs`
- `Scripts/Gameplay/AmbientSeaField.cs`
- `Scripts/Editor/StylizeBuilder.cs` (materials, LUT, ambient-field prefabs; menu **11. Stylize World**)

**Changed**
- `Environment/ProcMesh.cs`: trees, mountains, houses, lighthouse, rocks, smoothed-normal baking
- `Editor/EnvironmentBuilder.cs`, `GameplayBuilder.cs`, `CreaturesBuilder.cs`, `BonusBuilder.cs`: switch materials to the stylized shader, new camera rig, lane values
- `Gameplay/BoatController.cs`, `ObstacleSpawner.cs`, `OrbSpawner.cs`, `Bonus/HiddenOrbs.cs`: read `PlayArea`, density scaling (done in Step 1)
- `Shaders/StarlitOcean.shader`, `StarlitNightSky.shader`, `StarlitAtmosphere.shader`
- `Core/SettingsManager.cs`: the new quality toggles

To apply it in Unity: **Starlit Tides → Rebuild Everything**, as in the earlier phases.

---

## 9. Risks and decisions

| Risk | Mitigation |
|---|---|
| A wider lane makes the game easier (more room to dodge) | density scales with width, plus a quick bot playtest comparing average survival time before and after |
| Decorations confuse players about what's dangerous | decorations never use the coral or pink danger glow and never sit inside the lane; the reef line marks the edge |
| Outlines look noisy on small, far objects | outline width fades with distance and turns off past 80 m |
| A custom shader breaks SRP Batcher or shadows | follow the ocean shader's proven structure (ShadowCaster + DepthOnly + DepthNormals passes) |
| A full restyle is a lot of work | Steps 1–2 alone already fix most of the complaint; later steps are incremental |
