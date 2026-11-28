# Phase 9 — The Voyage: progression, living creatures, speed and calm sound

Feedback this phase answers:

1. The lane is crowded with creatures that appear at random, with no plan behind them.
2. Creatures should be enemies with their own attributes, and should act as they do in real life (jellyfish, dolphins, orcas, whales).
3. Creatures should be life-size.
4. The resolution setting can't select a resolution.
5. Sounds aren't soothing. The rain sounds bad.
6. Too many orbs make the game easy. It should get harder as you go.
7. The scene should change. Right now it's always the same place.
8. You can't feel the boat's speed. It needs effects, and the visuals should be prettier and more colourful.

---

## 1. Research summary

**Runner pacing.** Endless runners put **rest blocks** between bursts of challenge. After a rest comes a gentle "opener" obstacle, and new mechanics are held back until later. Difficulty is a few parameters ramped together: speed, obstacle density and the odds of each obstacle type. Levels are split into **sprints separated by rests**. ([GILP study](https://gamedeveloper.com/design/studying-gameplay-progression-on-runners), [Sure Footing](https://gamedeveloper.com/design/keep-running-procedural-level-generation-in-sure-footing), [runner course module](https://seanking11.github.io/gamebootcamp/modules/04-endless-runner))

**What the animals really do.**

| Animal | Real size | Real behaviour used in the game |
|---|---|---|
| Moon jellyfish | bell 25–40 cm | Weak swimmers that drift with the current, but they orient their swimming against it, which keeps a **bloom** together. ([Current Biology](https://www.australiangeographic.com.au/?p=16074), [bloom simulation](https://pmc.ncbi.nlm.nih.gov/articles/PMC6937341)) |
| Lion's mane jellyfish | bell up to ~2 m, very long tentacles | A lone drifter whose long tentacle trail stings. |
| Sea turtle (leatherback) | ~1.6–2 m | Swims below the surface and **comes up to breathe** for a few seconds. |
| Bottlenose dolphin | 2–3.9 m ([source](https://a-z-animals.com/animals/Bottlenose-Dolphin/)) | **Pods**. **Porpoising**: long leaps alternated with swimming near the surface, their "top gear". **Bow-riding** next to boats. ([surfacing behaviour](https://Www.Wikipedia.org/wiki/Spyhop), [common dolphin](https://www.discoverwildlife.com/animal-facts/marine-animals/common-dolphin)) |
| Giant manta ray | disc ~4.5–7 m ([size table](https://peerj.com/articles/715/)) | Slow glide. Mantas and devil rays **breach more than any other ray** ([UC Davis review](https://www.ucdavis.edu/news/sharks-and-rays-leap-out-water-many-reasons-including-feeding-courtship-and-communication)). |
| Orca | males 6–8 m, females 5–7 m ([PBS](https://www.pbs.org/wnet/nature/blog/killer-whale-fact-sheet)) | **Cooperative pod hunts**. Cruise at 6–8 km/h and **burst to ~45 km/h** for seconds ([UBC](https://mmru.ubc.ca/?p=45006)). **Wave-washing**: the pod speeds in side by side, dives at the last moment and the wave sweeps prey off the ice ([Nat Geo](https://www.nationalgeographic.com/animals/article/animals-up-close-wave-washing-killer-whales)). |
| Humpback whale | ~12–16 m (avg 12.3 m N. Pacific) | **Bubble-net feeding**: a ring of bubbles 3–30 m across, then a **vertical lunge** up through the middle. Up to a third of the body clears the water ([bubble-net](https://en.wikipedia.org/wiki/Bubble-net_feeding), [lunge study](https://fisherybulletin.nmfs.noaa.gov/sites/default/files/pdf-content/1982/802/hain.pdf)). **Breaching** ([NOAA Stellwagen](https://stellwagen.noaa.gov/visit/whalewatching/whale-behaviors.html)). |

**Sense of speed.** It comes from the camera and the surroundings more than from raw speed. The main tools are **FOV that widens with speed**, a short FOV punch on boosts, light camera shake, and streaks or objects passing close by. Motion blur on its own doesn't reliably help, and too much FOV or shake hurts. ([speed article](https://www.operationsports.com/why-is-speed-so-hard-to-capture-in-racing-games/), [Formula Fusion](https://www.unrealengine.com/developer-interviews/how-formula-fusion-nailed-the-feeling-of-fast), [motion-blur study](https://people.csail.mit.edu/lavanya/PDF/sharanetal13_MIG.pdf))

**Rain synthesis.** Rain is millions of tiny bubble sounds, which together sound like **pink noise**. Build it in layers: low and mid "boiling" (pink noise through band-pass filters whose centre wanders slowly), a wide stereo layer, and a few individual drops (10–20 ms decaying sine chirps). Finish with a gentle low-pass. ([Audiokinetic](https://www.audiokinetic.com/en/generating-rain-with-pure-synthesis), [raindrop generator](https://github.com/747745124/Raindrop-Generator))

---

## 2. The voyage: six zones, each with its own scene, enemies and rules

A run is now a **journey out to sea**. Every ~50 s you cross into a new zone: the sky, sea colours, scenery, weather and **which creatures live there** all change. A **5 s rest** sits between zones: no new hazards, a few orbs, and a banner naming the zone and what lives there. After the sixth zone the voyage loops back to zone 2 as **Voyage 2, 3…**, faster each loop.

| # | Zone | Look | New threat (taught here) | Tide speed |
|---|---|---|---|---|
| 1 | **Lantern Bay** | Moonlit bay, lighthouse, warm lanterns | Rocks and drifting logs only. Friendly dolphins **bow-ride** beside you (harmless). Hidden orbs in the lighthouse beam. | 8 m/s |
| 2 | **Glow Reef** | Turquoise/emerald glowing sea, green aurora, coral and algae. **The coast falls away behind you.** | **Moon-jelly blooms** (a drifting field with gaps), **sea turtles** surfacing to breathe | 10 |
| 3 | **Sunset Channel** | Magenta/orange sunset, sea stacks, fishing boats | **Dolphin pods porpoising** across, **manta rays** gliding and breaching | 12 |
| 4 | **Orca Strait** | Icy cyan sea, violet aurora, icebergs | **Orca hunts** (fins stalk, then the pod charges across), **wave-washing** (the pod dives and a wave shoves your boat sideways) | 14 |
| 5 | **Storm Reach** | Storm, rain, lightning, dark sea with electric glow | **Humpback bubble-net lunges** (a ring of bubbles warns, then the whale lunges up), **lion's mane jellies**, the giant whale breach | 16 |
| 6 | **Aurora Deep** | Full pink-green aurora, glowing ice and algae | Everything, mixed | 18 |
| 7+ | Voyage 2… (zones 2–6 again) | same looks | denser and shorter rests | +2 per loop (max 24) |

**Each zone introduces at most two new threats**, and they appear on their own first (the opener) before they're mixed with other hazards. That's the "teach, then test" rule from the runner research.

### Encounter director (fixes the crowding)

Creatures no longer come from a random roll on every obstacle spawn. An **encounter director** schedules them instead:

- **One encounter at a time.** Each zone has a list of encounters with weights, and a gap between encounters that shrinks over the voyage (7 s down to 3 s).
- During a **big** encounter (orca hunt, humpback lunge) the rock and log filler drops to 40%, so the creature *is* the challenge.
- Rocks and logs stay as filler, at a lower density than before.

### Creature roster (enemy attributes)

| Enemy | Size | Speed | Light lost | Behaviour | Warning / counterplay |
|---|---|---|---|---|---|
| Moon jelly | 0.4 m | drifts with the current, 0.6–1.2 m/s sideways | 8% | A bloom of 10–16 in a band across the lane with 1–2 gaps. The whole bloom drifts one way. | Read the gap and follow its drift |
| Lion's mane jelly | 2 m bell + 6 m tentacle trail | current drift | 20% | Single and slow. The long tentacle hitbox trails behind it. | Give it a wide berth, never pass *behind* it |
| Sea turtle | 1.8 m | 0.5 m/s across | 10% | Submerged (harmless) → surfaces 2.5 s to breathe → dives | Bubbles rise just before it surfaces |
| Dolphin pod | 3 m each, 3–6 animals | 8 m/s across (porpoising) | 15% | The pod crosses in a staggered line. A dolphin is harmless mid-leap but solid at the surface between leaps. | Time your pass to their leaps, or slip between two dolphins |
| Bow-riding dolphins | 3 m | match the boat | none | Swim and leap beside you (Lantern Bay, Sunset) | Pure atmosphere |
| Manta ray | 5.5 m | 0.6–2 m/s glide | 15% | Lazy S-curve; may breach and belly-flop | Its shadow and splash |
| Orca pod | 7.5 m male, 6 m females | stalk with the boat, then **12 m/s charge** | 35% | Stalk: tall dorsal fins slice along the lane edge (harmless). Charge: the pod turns and races across the lane in a staggered line. | 2.5 s of fins, then a splash cue as they turn |
| Orca wave-wash | — | wave crosses at 7 m/s | 0% (shoves the boat ~6 m) | The pod lines up and dives together, and a foaming wave rolls across the lane | See the foam front and steer into it, or get pushed into rocks |
| Humpback (bubble net) | 14 m | lunge 4 m up | 40% | A ring of bubbles (8 m across) appears ahead, then 2.4 s later the whale lunges up through it, mouth open, and sinks back | Don't be inside the ring when it fires |
| Giant glowing whale | (existing event) | — | — | Breach, then rows of waves with gaps | Existing, now only in Storm Reach and Aurora Deep |

The **narwhal** (an Arctic animal that doesn't breach in temperate bays) and the **nautilus** (a deep-sea animal that never surfaces) come out of the obstacle list. The humpback that breaches far out at sea stays as scenery, at 14 m.

### Light economy (fixes "too many orbs")

Before: drain was 0.035/s, but orbs offered about **0.37 light/s**, ten times the drain. Even missing 80% of orbs was safe.

Now the supply is tied to the drain:

| | Zone 1 | Zone 3 | Zone 6 | Voyage 2+ |
|---|---|---|---|---|
| Drain / s | 0.040 | 0.050 | 0.065 | 0.075 |
| Orb value | 10% | 9% | 7% | 6% |
| Orb groups every | 4.5 s | 4.0 s | 3.4 s | 3.2 s |
| Orbs per group | 4–5 | 3–5 | 3–4 | 3–4 |
| Supply ÷ drain | ~2.2× | ~1.8× | ~1.3× | ~1.1× |

Early you can miss half the orbs. Late you need almost all of them **and** no hits. Hidden orbs only appear while the lighthouse is in view (Lantern Bay).

---

## 3. Feeling the speed

- **FOV widens with tide speed** (+0 to +9°), with a stronger punch on dash and at each zone surge.
- **Speed streaks on the water**: glowing streaks lie on the sea near the boat and rush past at tide speed, so you see the motion.
- **Bow spray**: droplets kick up from the boat's bow, more of them at higher speed.
- **Air streaks**: faint speed lines at the screen edges, only at high speed and during dash.
- **Light camera shake** at high speed, and a short **chromatic aberration** pulse on dash and zone surges.
- **Zone surge**: entering a zone gives a brief speed burst with the banner.

## 4. Colour

Each zone has its own palette (fog, ambient light, moonlight, sky: sunset, aurora and storm amounts, and the sea's shallow, deep, foam and two bioluminescence colours), blended over 6 s at the zone border. Each zone also brings its own scenery: lanterns and boats; coral arches and glowing algae; sea stacks; **icebergs (new)**; dark stacks in the storm; icebergs and algae under the aurora.

## 5. Sound

All audio is still generated in code, now in **stereo**, rebuilt for calm:

- **Rain**: layered pink noise (low and mid "boiling" bands with slowly wandering filters), wide stereo, a few soft 10–20 ms drop chirps, and a gentle low-pass. No harsh hiss, no clicks. It also plays quieter.
- **Waves**: stereo surf whose brightness swells with each wave, and soft lapping.
- **Wind**: a gentle band-passed breath.
- **Music**: a warm pad with a slow attack, soft pentatonic bells and a reverb tail (seamless loop). The intense layer is a soft kalimba/marimba pattern instead of the sharp plucks.
- **Thunder**: a distant rumble with only a soft crack.

## 6. Resolution

Settings now has a **resolution dropdown** listing real sizes (native, then common widths at your screen's shape). In fullscreen the 3D renders at that size while the UI stays sharp. In windowed mode the window takes that size.

---

## 7. What was built (implementation)

Everything is generated by code, like earlier phases. **Starlit Tides > Rebuild Everything** now ends with **12. Build Voyage (Phase 9)**.

| Area | Files |
|---|---|
| Zones, looks, pacing, coast departure | `Scripts/Core/VoyageDirector.cs` (all zone numbers live in `DefaultZones()`) |
| Difficulty read from the zone | `Scripts/Core/DifficultyManager.cs`, `Gameplay/ObstacleSpawner.cs`, `Gameplay/OrbSpawner.cs`, `Bonus/HiddenOrbs.cs` |
| Planned creature encounters, orca wave-wash | `Scripts/Gameplay/EncounterDirector.cs` |
| Real behaviours (surfacing, porpoising, stalk-and-charge, bubble-net lunge, bow-riding) | `Scripts/Gameplay/SeaCreature.cs`, `Gameplay/Jellyfish.cs` (current drift) |
| Life-size creatures, female orca, humpback | `Scripts/Editor/CreaturesBuilder.cs`, `Editor/Phase9Builder.cs` (moon jelly, lion's mane) |
| Speed feel | `Scripts/CameraFX/SpeedFx.cs`, `Features/CameraAspectFit.cs` (FOV), `Features/CameraAnchor.cs` (shake), `Editor/VfxKit.cs` (streaks, spray, air lines, bubble net) |
| Scenery per zone (icebergs, coral) | `Scripts/Gameplay/AmbientSeaField.cs`, `Editor/SeaFieldBuilder.cs` |
| Zone banner, hints | `Scripts/UI/HudView.cs`, `Editor/UIBuilder.cs` |
| Calm stereo audio | `Scripts/Bonus/ProceduralAudio.cs`, `Bonus/AudioManager.cs` |
| Resolution dropdown | `Scripts/Core/DisplayManager.cs`, `UI/SettingsView.cs`, `Editor/UIBuilder.cs` |

### How it was verified

- Batch compile, then Rebuild Everything with no errors (asset check passed).
- A batch play-mode harness drove menu → Play → intro skip → a full voyage and logged every second: zone, tide, encounter, filler scale, orb count and every creature's position, phase and solidity. It also rendered screenshots per zone.
  - The six zones changed in order, with the right banners. The natural Lantern Bay → Glow Reef transition worked.
  - Orcas stalk at the lane edge (not solid), turn, then charge across at ~12 m/s through the boat's row (solid).
  - The humpback bubble net glows for 2.4 s, then the whale lunges up next to the boat. In the harness it hit the idle boat.
  - Moon-jelly blooms spawn ~25 jellies on one shared current.
  - Orbs on screen dropped from ~70 to ~10–17.
- Resolution: selecting each dropdown entry changes the shown value, the chosen size and the saved setting.
- Audio, analysed offline: no clipping, seamless loops, ~4 s total generation on a background thread. The rain's spectral brightness is ~1.6 kHz; the old rain was white noise high-passed at 1.2 kHz.
- **Not verified here:** how it sounds (no speakers in batch), real frame rate (batch doesn't render every frame), and the HUD banner's look (overlay UI isn't in camera renders). Check these in the editor or a build.


---

## 8. Update: harder light economy, orb chains, boat wake

**Light drain (+25%).** The drain is now 0.050 per second in Lantern Bay, then 0.054, 0.058, 0.063 and 0.068, reaching 0.072 in Aurora Deep. Each voyage loop adds +0.006.

**Orb chains.** Orbs arrive one at a time in chains of 5–8:
- They are 1.30 s apart in zone 1, closing to 1.05 s in zone 6.
- Each orb sits sideways from the last at **45–85% of the distance the boat can really steer in that gap**. That reach is the steering speed (boosted on fast tides) times the gap, minus ~0.3 s to reverse. Seven times in ten the chain zig-zags, forcing a full reversal.
- A 2.6–3.6 s calm follows each chain.
- The **combo window is 2.0 s** (it was 2.6 s), so missing a single orb breaks the combo. A full combo is possible, but only with near-perfect steering while dodging.
- Orbs give 10% light, falling to 9% late. If you catch every orb, the supply is about 1.4× the drain in zone 1 and about 1.1× in Aurora Deep. Catching roughly 70% keeps you alive early; late zones need almost every orb and no hits.

**Boat wake** (`BoatSplash.cs`), based on how real boats make waves:
- **Kelvin wake.** The two arms of the V leave the bow at **19.47° = arcsin(1/3)** to the boat's path through the water ([Kelvin wake pattern](https://en.wikipedia.org/wiki/Kelvin_wake_pattern)). They stay correct while steering because the path is measured *through the water* (boat velocity plus tide).
- **Turbulent foam band** behind the stern.
- **Two layers:** white foam plus glowing plankton, **emitted per metre travelled**. That is the approach the Blood Stone team used for even trails at any speed ([source](https://cgw.com/Press-Center/Web-Exclusives/2010/Spy-Game-Blood-Stone-s-water-effects.aspx)).
- Every wake particle is placed on the wave surface each frame.
- **Turn spray** flies off the outside of hard turns, alongside the existing bow spray.


---

## 9. Update: a fair start that ramps up, throttle and dash

**Why the light drained too fast.** Orb chains offer about 6.5 orbs per ~10 s. With orbs at 10% light, that is at most 0.063 light/s against a drain of 0.050/s, and orbs spaced at 45–85% of the boat's reach are hard to catch. A realistic 50–60% catch rate meant the meter ran out in about a minute, even when playing well.

**New economy.** Supply is about 0.71 orbs/s: chains of 6.5 orbs, 1.1 s apart, then a 3.1 s break. Each zone breaks even at a higher catch rate:

| Zone | Drain/s | Orb | Sideways spacing (of reach) | Break-even catch |
|---|---|---|---|---|
| 1 Lantern Bay | 0.034 | 12% | 20–45% | 40% |
| 2 Glow Reef | 0.039 | 11.5% | 25–55% | 48% |
| 3 Sunset Channel | 0.043 | 11% | 30–62% | 55% |
| 4 Orca Strait | 0.046 | 10.5% | 35–70% | 62% |
| 5 Storm Reach | 0.048 | 10% | 40–78% | 68% |
| 6 Aurora Deep | 0.053 | 10% | 45–85% | 75% |

Each voyage loop adds +0.004 drain and +3% spacing. Lantern Bay and Glow Reef also have fewer rocks.

**Throttle.** Holding forward makes the whole sea rush past up to **35% faster**, so you cover and score more distance for the same light. It also widens the view by 4° and adds speed lines, and the camera follows only 40% of the boat's forward movement, so the boat visibly pulls ahead. Holding back slows the sea by up to 20%.

**Dash.** The dash works left, right, forward and diagonally, and near-pure directions snap clean. A forward dash adds a 60% speed surge that fades in about 0.7 s. Every dash gives a field-of-view punch, a burst of speed lines and a lens pulse.

**Verified** in a play-mode run with a virtual keyboard:
- holding W: tide 8.0 → 10.8 m/s, the boat 13 m forward with the camera 5 m
- forward dash: tide peaked at 12.6 m/s and the view widened +9.4°
- left/right dashes: 7 m and 6 m

## 10. Update: every graphics and power setting is the player's choice

Earlier optimisation passes picked fixed values to keep laptops cool (output capped at 1920 px, SMAA only, smaller shadows and reflections). Those are now the player's choice: the Settings screen has two columns and every choice can be changed.

| Setting | Choices | Default |
|---|---|---|
| Display resolution | native (e.g. 2940 x 1846) and common widths below it | native |
| 3D resolution | 100%, 90% … 50% of the display resolution (AMD FSR upscale) | 100% |
| Anti-aliasing | Off, SMAA, MSAA 2x, MSAA 4x | SMAA |
| Shadows | Off, Low, Medium, High, Ultra (512 → 4096 px, 1 → 4 cascades, 35 → 100 m) | from the preset (High) |
| Reflections | Low 128, High 256, Ultra 512 px | from the preset (High) |
| Colour depth | Standard (32-bit HDR), High (64-bit, less banding) | Standard |
| Frame rate | 30, 60, Match display (vSync), Unlimited | Match display |
| Battery saver when unplugged (30 fps, fewer effects) | on / off | off |
| 30 fps in menus | on / off | on |
| Lower 3D resolution if the game slows | on / off | on |

The quality preset still controls grass, rain, bloom, god rays, ocean detail and scenery density, and resets shadows and reflections to its own values. AUTO-DETECT tunes the 3D resolution and preset at the player's display resolution.

Code: `SettingsManager` (stores the options and applies MSAA, shadows and colour depth to the active URP asset, restored after play mode in the editor), `DisplayManager` (display resolution and 3D resolution are now separate), `PowerManager` (frame rate, menu frame rate, battery saver), `SettingsView` and `UIBuilder.BuildSettings` (the two-column screen).

**Verified** in a play-mode run that changed every option through the UI: 28 of 28 checks passed (URP asset values, camera anti-aliasing, probe size, vSync and frame cap, saved preferences, preset reset), and the URP assets were unchanged afterwards.

### Apply button and release check

Settings changes now wait for **APPLY**. The screen edits a pending copy, APPLY applies it (the preset first, so the player's own shadow and reflection choices are kept), and BACK drops anything not applied. The volume sliders stay live, so players can hear the level while they drag. Verified in play mode: 13 of 13 checks.

**Release check** (`-starlitDiag -starlitTour`): the built game photographs the menu, the Settings screen and all six zones with its own screenshot, presses PLAY like a player, counts every error, exception and failed assertion, and quits. Mac build result: **9 screenshots, 0 errors/exceptions, 0 render-check warnings**. The screenshots are in `ReleaseCheck/` (JPEG).

Found and fixed by this check:
- The Windows (DirectX) shader compile warned about `pow()` of a possibly negative number in the jellyfish shader (NaN pixels on DirectX) and about screen-space derivatives inside the god-ray loop. Both are fixed, and the Windows build now compiles our shaders with no warnings.
- A menu screen could stay on top of a run that wasn't started with the PLAY button. Any menu now hides when a run starts.

### Fix: blurry fullscreen on battery

On a MacBook running on battery, fullscreen looked low-res. The log showed 1440 x 904 even though the player had chosen native 2940 x 1846. The battery saver (then on by default) capped the output at 1440 px and drew the 3D at 85% of that, while the Settings screen still showed the player's own choice. Picking those values again changed nothing, so APPLY stayed disabled. Now:
- The battery saver is off by default. When on, it never lowers the resolution; it caps the frame rate at 30 and switches off god rays and some far scenery. The toggle says "(30 fps)" and the detail line shows when it is active.
- The default display resolution is native. Auto-detect picks the 3D resolution that keeps 60 fps.

Verified on battery: the release check ran fullscreen at 2940 x 1846 with 0 errors, and the screenshots in `ReleaseCheck/` were refreshed.

## 11. Update: rarer dash, collisions that cost light, a real jellyfish

**Dash.** The cooldown is 2.5 s (it was 1.1 s) and the untouchable window is 0.2 s (it was 0.3 s). With a dash every second, players could dash through almost everything, so collisions rarely happened.

**Collisions.** Every hit already took light, but the cost was invisible and the small creatures barely cost anything. Now:
- the light ring flashes red and jolts, and "-25% LIGHT" (the real amount) fades out under it;
- minimum costs: moon jelly 15% (was 8%), turtle 15% (was 10%), dolphin and manta 20% (was 15%), log 22% (was 20%), lion's mane 30% (was 20%). Rocks stay at 25%, orcas at 35%, the humpback at 40%.

Verified in play mode by putting each obstacle type on the boat: rock, log, moon jelly, lion's mane, turtle (once surfaced; it is harmless underwater by design), dolphin, manta, both orcas and the humpback. Each hit took exactly its set amount, and the HUD showed the cost.

**Jellyfish redesign** (`StarlitJellyBell.shader`, `Phase9Builder.JellyBellMesh`). The old jelly was a small blob of pure glow with a round glow sprite, which is exactly what an orb looks like. The new one is modelled on a real moon jelly (*Aurelia*):
- a wide, flat, translucent bell (alpha-blended, so the sea shows through);
- the pink four-leaf-clover gonads on top, fine radial canals and a scalloped rim with spots of light and a wave of light running round it;
- 56 fine fringe tentacles and four frilly oral arms;
- a real pulse: a quick contraction (narrower, taller) and a slow relaxation, with tentacles that lag behind.

It is violet and pink, while orbs are white and cyan. It is about 1.2 m across, bigger than life so it reads at speed. The lion's mane uses the same model in amber and red, about 2.9 m across, with 120 long tentacles. Comparison picture: `ReleaseCheck/jellyfish_vs_orb.jpg`.
