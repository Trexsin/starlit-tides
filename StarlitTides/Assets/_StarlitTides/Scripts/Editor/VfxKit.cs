// =============================================================================
//  VfxKit - the Starlit Tides effects toolkit (VFX polish pass)
// -----------------------------------------------------------------------------
//  Every particle effect in the game is built here, so they share one look:
//
//  TEXTURES (generated in code, white with the shape in alpha):
//    glow     soft round light with a bright centre
//    sparkle  four-point star with thin diffraction spikes and a faint halo
//    ring     thin soft ring: shockwaves and ripples on the water
//    droplet  a teardrop for stretched spray
//    mist     2x2 flipbook of soft noise puffs (random frame per particle)
//    streak   a tapered line for speed lines and light rays
//    flare    an anamorphic horizontal flare for flashes
//    trail    a soft line for particle light trails
//
//  MATERIALS use StarlitTides/Vfx (additive "light" or premultiplied "matter"),
//  and are updated IN PLACE (same asset, same GUID), so prefabs built by an
//  earlier step never lose their materials when a later step runs.
//
//  EFFECTS are LAYERED: a real-looking burst is a flash + a shockwave + sparks +
//  trails + mist, each with its own timing, size and colour curves. The one-shot
//  effects keep their old prefab paths, so every script that plays them is
//  unchanged.
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarlitTides.EditorTools
{
    public static class VfxKit
    {
        const string Dir = "Assets/_StarlitTides/Generated/VFX";
        const string ShaderName = "StarlitTides/Vfx";

        static bool ready;
        public static Texture2D Glow, Sparkle, Ring, Droplet, Mist, Streak, Flare, Trail;

        // ==================================================================
        // TEXTURES
        // ==================================================================
        public static void Ensure()
        {
            if (ready && Glow != null && Sparkle != null) return;
            EnsureFolder(Dir);
            Glow = Tex("T_VfxGlow", 128, 128, (x, y) =>
            {
                float d2 = x * x + y * y;
                return Mathf.Exp(-d2 * 4.5f) * 0.75f + Mathf.Exp(-d2 * 38f) * 0.6f;
            });
            Sparkle = Tex("T_VfxSparkle", 256, 256, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float core = Mathf.Exp(-d * d * 90f);
                float halo = Mathf.Exp(-d * d * 9f) * 0.22f;
                // long thin cross spikes, shorter diagonal ones
                float spikeH = Mathf.Exp(-(y * y) / 0.00018f) * Mathf.Exp(-Mathf.Abs(x) * 3.2f);
                float spikeV = Mathf.Exp(-(x * x) / 0.00018f) * Mathf.Exp(-Mathf.Abs(y) * 3.2f);
                float u = (x + y) * 0.7071f, v = (x - y) * 0.7071f;
                float diag = (Mathf.Exp(-(v * v) / 0.0001f) * Mathf.Exp(-Mathf.Abs(u) * 7f) + Mathf.Exp(-(u * u) / 0.0001f) * Mathf.Exp(-Mathf.Abs(v) * 7f)) * 0.35f;
                return core + halo + (spikeH + spikeV) * 0.9f + diag;
            });
            Ring = Tex("T_VfxRing", 256, 256, (x, y) =>
            {
                float d = Mathf.Sqrt(x * x + y * y);
                float r = (d - 0.78f) / 0.05f;
                return Mathf.Exp(-r * r) + Mathf.Exp(-((d - 0.7f) * (d - 0.7f)) / 0.02f) * 0.18f;
            });
            Droplet = Tex("T_VfxDroplet", 64, 128, (x, y) =>
            {
                // y from -1 (tail) to 1 (head): a bright round head and a fading tail
                float w = Mathf.Lerp(0.15f, 0.55f, (y + 1f) * 0.5f);
                float body = Mathf.Exp(-(x * x) / (w * w * 0.35f)) * Mathf.SmoothStep(0f, 1f, (y + 1f) * 0.6f) * Mathf.SmoothStep(0f, 1f, (1f - y) * 3f);
                float head = Mathf.Exp(-(x * x + (y - 0.62f) * (y - 0.62f)) * 22f);
                return Mathf.Max(body * 0.8f, head);
            });
            Streak = Tex("T_VfxStreak", 256, 32, (x, y) =>
                Mathf.Exp(-(y * y) * 18f) * Mathf.Pow(Mathf.Max(0f, 1f - Mathf.Abs(x)), 1.6f));
            Flare = Tex("T_VfxFlare", 256, 64, (x, y) =>
                Mathf.Exp(-(y * y) * 30f) * (Mathf.Exp(-Mathf.Abs(x) * 3.5f) * 0.8f + Mathf.Exp(-x * x * 60f) * 0.6f));
            Trail = Tex("T_VfxTrail", 64, 32, (x, y) => Mathf.Exp(-(y * y) * 9f));
            Mist = MistSheet("T_VfxMist");
            ready = true;
        }

        // Builds a texture from f(x, y) with x, y in -1..1; white RGB, shape in alpha.
        static Texture2D Tex(string name, int w, int h, System.Func<float, float, float> f)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true);
            for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                float x = (i + 0.5f) / w * 2f - 1f, y = (j + 0.5f) / h * 2f - 1f;
                float edge = Mathf.Clamp01((1f - Mathf.Max(Mathf.Abs(x), Mathf.Abs(y))) * 12f);   // zero at the border
                t.SetPixel(i, j, new Color(1f, 1f, 1f, Mathf.Clamp01(f(x, y)) * edge));
            }
            return SaveTex(t, name);
        }

        // Four soft noise puffs in a 2x2 sheet (random frame per particle).
        static Texture2D MistSheet(string name)
        {
            const int s = 128;
            var t = new Texture2D(s * 2, s * 2, TextureFormat.RGBA32, true);
            for (int f = 0; f < 4; f++)
            {
                int ox = (f % 2) * s, oy = (f / 2) * s;
                float seed = f * 17.3f;
                for (int j = 0; j < s; j++)
                for (int i = 0; i < s; i++)
                {
                    float x = (i + 0.5f) / s * 2f - 1f, y = (j + 0.5f) / s * 2f - 1f;
                    float n = 0f, amp = 0.55f, freq = 2.2f;
                    for (int o = 0; o < 4; o++) { n += Mathf.PerlinNoise(x * freq + seed, y * freq - seed) * amp; amp *= 0.5f; freq *= 2.1f; }
                    float radial = Mathf.Clamp01(1f - Mathf.Sqrt(x * x + y * y) * (0.95f + (n - 0.5f) * 0.9f));
                    float a = Mathf.SmoothStep(0f, 1f, radial) * Mathf.Clamp01(n * 1.6f - 0.15f);
                    t.SetPixel(ox + i, oy + j, new Color(1f, 1f, 1f, a));
                }
            }
            return SaveTex(t, name);
        }

        static Texture2D SaveTex(Texture2D t, string name)
        {
            t.Apply();
            string path = Dir + "/" + name + ".png";
            bool existed = File.Exists(path);
            File.WriteAllBytes(path, t.EncodeToPNG());   // same file, same GUID
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (!existed || imp.alphaIsTransparency == false)
            {
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.mipmapEnabled = true;
                imp.filterMode = FilterMode.Trilinear;
                imp.textureCompression = TextureImporterCompression.Uncompressed;   // soft gradients band when compressed
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ==================================================================
        // MATERIALS (created once, then updated in place)
        // ==================================================================
        public static Material Mat(string name, Texture2D tex, Color tint, bool alpha = false, float core = 1.5f, float soft = 0.6f, float near = 1.2f)
        {
            Ensure();
            string path = Dir + "/" + name + ".mat";
            Shader shader = Shader.Find(ShaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }
            else if (m.shader != shader) m.shader = shader;
            m.SetTexture("_MainTex", tex);
            m.SetColor("_TintColor", tint);
            m.SetFloat("_CoreBoost", alpha ? 0f : core);
            m.SetFloat("_SoftFade", soft);
            m.SetFloat("_NearFade", near);
            m.SetFloat("_AlphaMode", alpha ? 1f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.One);
            m.SetFloat("_DstBlend", (float)(alpha ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
            if (alpha) m.EnableKeyword("_VFX_ALPHA"); else m.DisableKeyword("_VFX_ALPHA");
            m.renderQueue = alpha ? 3000 : 3010;   // light draws over matter
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ==================================================================
        // SMALL HELPERS
        // ==================================================================
        static ParticleSystem Sys(string name, Transform parent, Material mat, ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false; main.playOnAwake = false; main.duration = 0.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var em = ps.emission; em.rateOverTime = 0f;
            var sh = ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = mode;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingFudge = mode == ParticleSystemRenderMode.HorizontalBillboard ? 10f : 0f;
            return ps;
        }

        static void Burst(ParticleSystem ps, int count, float time = 0f)
        {
            var em = ps.emission;
            var list = new ParticleSystem.Burst[em.burstCount + 1];
            em.GetBursts(list);
            list[list.Length - 1] = new ParticleSystem.Burst(time, (short)count);
            em.SetBursts(list);
        }

        static ParticleSystem.MinMaxCurve R(float a, float b) { return new ParticleSystem.MinMaxCurve(a, b); }

        static ParticleSystem.MinMaxCurve Curve(params float[] tv)
        {
            var c = new AnimationCurve();
            for (int i = 0; i + 1 < tv.Length; i += 2) c.AddKey(new Keyframe(tv[i], tv[i + 1]));
            for (int i = 0; i < c.length; i++) c.SmoothTangents(i, 0f);
            return new ParticleSystem.MinMaxCurve(1f, c);
        }

        // colours at evenly spaced times; alpha as (time, value) pairs
        static Gradient Grad(Color[] colors, params float[] alphaTV)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) ck[i] = new GradientColorKey(colors[i], colors.Length == 1 ? 0f : i / (colors.Length - 1f));
            var ak = new GradientAlphaKey[alphaTV.Length / 2];
            for (int i = 0; i < ak.Length; i++) ak[i] = new GradientAlphaKey(alphaTV[i * 2 + 1], alphaTV[i * 2]);
            g.SetKeys(ck, ak);
            return g;
        }

        static void ColorLife(ParticleSystem ps, Gradient g) { var c = ps.colorOverLifetime; c.enabled = true; c.color = g; }
        static void SizeLife(ParticleSystem ps, ParticleSystem.MinMaxCurve c) { var s = ps.sizeOverLifetime; s.enabled = true; s.size = c; }
        static void Drag(ParticleSystem ps, float drag) { var l = ps.limitVelocityOverLifetime; l.enabled = true; l.drag = drag; l.limit = 1000f; }
        static void Spin(ParticleSystem ps, float degPerSec)
        {
            var m = ps.main; m.startRotation = R(0f, Mathf.PI * 2f);
            var r = ps.rotationOverLifetime; r.enabled = true; r.z = R(-degPerSec * Mathf.Deg2Rad, degPerSec * Mathf.Deg2Rad);
        }
        static void Twinkle(ParticleSystem ps, float amount = 0.6f)
        {
            // strength scales every noise channel, so keep it at 1 and zero the position channel
            var n = ps.noise; n.enabled = true; n.strength = 1f; n.positionAmount = 0f; n.rotationAmount = 0f; n.sizeAmount = amount;
            n.frequency = 4f; n.scrollSpeed = 3f; n.quality = ParticleSystemNoiseQuality.Medium;
        }
        // Velocity over lifetime. Unity requires x, y and z (and the three orbital axes)
        // to use the same curve mode, so always set all of them as ranges.
        static void Vel(ParticleSystem ps, Vector2 x, Vector2 y, Vector2 z, Vector2 orbitalY = default, Vector2 radial = default)
        {
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = R(x.x, x.y); v.y = R(y.x, y.y); v.z = R(z.x, z.y);
            v.orbitalX = R(0f, 0f); v.orbitalY = R(orbitalY.x, orbitalY.y); v.orbitalZ = R(0f, 0f);
            v.radial = R(radial.x, radial.y);
        }

        static void Trails(ParticleSystem ps, Material mat, float lifetime, float width = 1f)
        {
            var t = ps.trails; t.enabled = true; t.ratio = 1f; t.lifetime = lifetime; t.dieWithParticles = true;
            t.inheritParticleColor = true; t.sizeAffectsWidth = true; t.widthOverTrail = Curve(0f, width, 1f, 0f);
            t.colorOverTrail = new ParticleSystem.MinMaxGradient(Grad(new[] { Color.white, Color.white }, 0f, 1f, 1f, 0f));
            t.minVertexDistance = 0.05f;
            ps.GetComponent<ParticleSystemRenderer>().trailMaterial = mat;
        }
        static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, float angle = 25f, Vector3 rotation = default)
        {
            var s = ps.shape; s.enabled = true; s.shapeType = type; s.radius = radius; s.angle = angle; s.rotation = rotation;
        }
        static GameObject Finish(ParticleSystem root)
        {
            var pooled = root.gameObject.AddComponent<PooledParticle>();
            pooled.snapToWater = pendingSnaps.ToArray();
            pendingSnaps.Clear();
            return root.gameObject;
        }

        static Material RingMat(string name, Color tint, bool alpha = false) { return Mat(name, Ring, tint, alpha, 1f, 0.04f); }

        static readonly Color White = Color.white;
        static readonly Color Cyan = new Color(0.45f, 1f, 1f);
        static readonly Color Teal = new Color(0.15f, 0.75f, 1f);
        static readonly Color Mint = new Color(0.55f, 1f, 0.8f);
        static readonly Color Gold = new Color(1f, 0.82f, 0.4f);
        static readonly Color Amber = new Color(1f, 0.55f, 0.18f);
        static readonly Color Coral = new Color(1f, 0.45f, 0.38f);
        static readonly Color Rose = new Color(1f, 0.55f, 0.8f);

        // A flash: an anamorphic flare and a round glow, both popping and fading fast.
        static void Flash(Transform parent, Color hdrTint, float width, float glowSize, float life, string prefix)
        {
            var flare = Sys(prefix + " Flare", parent, Mat("M_Vfx" + prefix + "Flare", Flare, hdrTint, false, 2.5f));
            var fm = flare.main; fm.startLifetime = life; fm.startSpeed = 0f;
            fm.startSize3D = true; fm.startSizeX = width; fm.startSizeY = width * 0.12f; fm.startSizeZ = 1f;
            Burst(flare, 1);
            SizeLife(flare, Curve(0f, 0.3f, 0.12f, 1f, 1f, 0.15f));
            ColorLife(flare, Grad(new[] { White, White }, 0f, 1f, 0.5f, 0.6f, 1f, 0f));

            var glow = Sys(prefix + " Glow", parent, Mat("M_Vfx" + prefix + "Glow", Glow, hdrTint, false, 3f));
            var gm = glow.main; gm.startLifetime = life * 1.2f; gm.startSpeed = 0f; gm.startSize = glowSize;
            Burst(glow, 1);
            SizeLife(glow, Curve(0f, 0.4f, 0.15f, 1f, 1f, 0.3f));
            ColorLife(glow, Grad(new[] { White, White }, 0f, 1f, 1f, 0f));
        }

        // Rings that PooledParticle snaps onto the real wave height when the effect plays.
        static readonly List<Transform> pendingSnaps = new List<Transform>();

        // A shockwave ring lying on the water. Its material must use a tiny soft-fade
        // distance (RingMat), or the soft-particle fade would hide a ring that lies
        // exactly on the surface.
        static ParticleSystem WaterRing(Transform parent, string name, Material mat, float from, float to, float life, float y, float delay = 0f)
        {
            var ring = Sys(name, parent, mat, ParticleSystemRenderMode.HorizontalBillboard);
            pendingSnaps.Add(ring.transform);
            ring.transform.localPosition = new Vector3(0f, y, 0f);
            var m = ring.main; m.startLifetime = life; m.startSpeed = 0f; m.startSize = to;
            Burst(ring, 1, delay);
            SizeLife(ring, Curve(0f, from / to, 0.35f, 0.75f, 1f, 1f));
            ColorLife(ring, Grad(new[] { White, White }, 0f, 0f, 0.06f, 1f, 0.5f, 0.55f, 1f, 0f));
            return ring;
        }

        // ==================================================================
        // ONE-SHOT EFFECTS
        // ==================================================================

        // Collecting a plankton orb: the most frequent effect, so it has to sing.
        public static GameObject OrbBurst()
        {
            Ensure();
            var root = Sys("VFX_OrbBurst", null, Mat("M_VfxOrbSpark", Sparkle, new Color(0.8f, 2.4f, 2.8f), false, 2.2f));
            var m = root.main; m.duration = 0.3f;
            m.startLifetime = R(0.7f, 1.3f); m.startSpeed = R(1.5f, 3.6f); m.startSize = R(0.28f, 0.55f);
            m.startColor = new ParticleSystem.MinMaxGradient(White, Mint);
            m.gravityModifier = -0.12f;
            Burst(root, 16);
            Shape(root, ParticleSystemShapeType.Sphere, 0.3f);
            Vel(root, Vector2.zero, new Vector2(0.6f, 2f), Vector2.zero, new Vector2(2f, 4f), new Vector2(0.2f, 0.8f));
            Drag(root, 1.8f); Spin(root, 120f); Twinkle(root, 0.5f);
            SizeLife(root, Curve(0f, 0f, 0.08f, 1f, 0.55f, 0.75f, 1f, 0f));
            ColorLife(root, Grad(new[] { White, Cyan, Teal }, 0f, 1f, 0.7f, 0.85f, 1f, 0f));

            Flash(root.transform, new Color(1.2f, 2.6f, 3f), 3.4f, 2.6f, 0.32f, "Orb");
            WaterRing(root.transform, "Shockwave", RingMat("M_VfxOrbRing", new Color(0.4f, 2f, 2.4f), false), 0.4f, 5.5f, 0.7f, -0.5f);

            var motes = Sys("Light Trails", root.transform, Mat("M_VfxOrbMote", Glow, new Color(0.6f, 2.2f, 2.6f), false, 3f));
            var mm = motes.main; mm.startLifetime = R(0.55f, 1.0f); mm.startSpeed = R(3f, 6.5f); mm.startSize = R(0.07f, 0.13f);
            mm.gravityModifier = -0.35f; mm.startColor = new ParticleSystem.MinMaxGradient(White, Cyan);
            Burst(motes, 22);
            Shape(motes, ParticleSystemShapeType.Sphere, 0.2f);
            Drag(motes, 2.2f);
            ColorLife(motes, Grad(new[] { White, Cyan }, 0f, 1f, 0.7f, 0.9f, 1f, 0f));
            Trails(motes, Mat("M_VfxOrbTrail", Trail, new Color(0.4f, 1.8f, 2.2f), false, 0.5f), 0.22f);
            return Finish(root);
        }

        // Water splash (creatures landing). Hit = splash + warm danger sparks.
        public static GameObject Splash(bool hit = false)
        {
            Ensure();
            string name = hit ? "VFX_Hit" : "VFX_Splash";
            var root = Sys(name, null, Mat("M_VfxSpray", Droplet, new Color(0.78f, 0.92f, 1f, 0.95f), true), ParticleSystemRenderMode.Stretch);
            var m = root.main; m.duration = 0.3f;
            m.startLifetime = R(0.55f, 1.0f); m.startSpeed = R(3.8f, 8f); m.startSize = R(0.07f, 0.15f);
            m.gravityModifier = 1.6f;
            Burst(root, 38);
            Shape(root, ParticleSystemShapeType.Cone, 0.5f, 28f, new Vector3(-90f, 0f, 0f));
            var rr = root.GetComponent<ParticleSystemRenderer>(); rr.velocityScale = 0.07f; rr.lengthScale = 1.1f;
            ColorLife(root, Grad(new[] { White, White }, 0f, 1f, 0.75f, 0.9f, 1f, 0f));

            WaterRing(root.transform, "Foam Ring", RingMat("M_VfxFoamRing", new Color(0.85f, 0.95f, 1f, 0.85f), true), 0.6f, 4.2f, 0.85f, 0.05f);

            var mist = Sys("Mist", root.transform, Mat("M_VfxMist", Mist, new Color(0.7f, 0.84f, 1f, 0.42f), true, 0f, 1.2f));
            var mm = mist.main; mm.startLifetime = R(0.8f, 1.4f); mm.startSpeed = R(0.5f, 1.6f); mm.startSize = R(1.1f, 2.1f);
            Burst(mist, 6);
            Shape(mist, ParticleSystemShapeType.Hemisphere, 0.6f);
            Spin(mist, 25f);
            var ts = mist.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 2; ts.numTilesY = 2; ts.frameOverTime = R(0f, 0.999f);
            SizeLife(mist, Curve(0f, 0.5f, 1f, 1.6f));
            ColorLife(mist, Grad(new[] { White, White }, 0f, 0f, 0.1f, 1f, 1f, 0f));

            var sparks = Sys("Plankton Sparks", root.transform, Mat("M_VfxSplashSpark", Sparkle, new Color(0.5f, 2.2f, 2.6f), false, 2f));
            var sm = sparks.main; sm.startLifetime = R(0.5f, 0.9f); sm.startSpeed = R(2f, 5f); sm.startSize = R(0.2f, 0.4f); sm.gravityModifier = 0.6f;
            Burst(sparks, 18);
            Shape(sparks, ParticleSystemShapeType.Cone, 0.4f, 70f, new Vector3(-90f, 0f, 0f));
            Drag(sparks, 1.5f); Spin(sparks, 90f);
            ColorLife(sparks, Grad(new[] { White, Cyan }, 0f, 1f, 0.6f, 0.8f, 1f, 0f));

            if (hit)
            {
                Flash(root.transform, new Color(3f, 1.1f, 0.8f), 3.8f, 3.2f, 0.26f, "Hit");
                var coral = Sys("Danger Sparks", root.transform, Mat("M_VfxHitSpark", Sparkle, new Color(3f, 1.1f, 0.9f), false, 2.5f));
                var cm = coral.main; cm.startLifetime = R(0.35f, 0.7f); cm.startSpeed = R(4f, 9f); cm.startSize = R(0.25f, 0.5f);
                cm.startColor = new ParticleSystem.MinMaxGradient(White, Rose);
                Burst(coral, 18);
                Shape(coral, ParticleSystemShapeType.Sphere, 0.4f);
                Drag(coral, 3f); Spin(coral, 200f);
                ColorLife(coral, Grad(new[] { White, Coral }, 0f, 1f, 0.5f, 0.8f, 1f, 0f));
                Trails(coral, Mat("M_VfxHitTrail", Trail, new Color(2.4f, 0.8f, 0.6f), false, 0.4f), 0.18f);
                WaterRing(root.transform, "Danger Ring", RingMat("M_VfxHitRing", new Color(2.6f, 0.9f, 0.7f), false), 0.5f, 4.8f, 0.5f, 0.06f);
            }
            return Finish(root);
        }

        public static GameObject Hit() { return Splash(true); }

        // Dash: speed lines streaming back along the lane, a gold ring and sparks.
        public static GameObject DashBurst()
        {
            Ensure();
            var root = Sys("VFX_DashBurst", null, Mat("M_VfxSpeedLine", Streak, new Color(2.6f, 2f, 1.2f), false, 1.5f), ParticleSystemRenderMode.Stretch);
            var m = root.main; m.duration = 0.2f;
            m.startLifetime = R(0.22f, 0.42f); m.startSpeed = R(1f, 2f); m.startSize = R(0.06f, 0.12f);
            Burst(root, 24);
            Shape(root, ParticleSystemShapeType.Circle, 1.4f, 0f, new Vector3(90f, 0f, 0f));
            Vel(root, Vector2.zero, new Vector2(0.2f, 1.2f), new Vector2(-16f, -9f));
            var rr = root.GetComponent<ParticleSystemRenderer>(); rr.velocityScale = 0.09f; rr.lengthScale = 2.5f;
            ColorLife(root, Grad(new[] { White, Gold }, 0f, 0f, 0.15f, 1f, 1f, 0f));

            Flash(root.transform, new Color(3f, 2.3f, 1.1f), 3f, 2.2f, 0.2f, "Dash");
            WaterRing(root.transform, "Gold Ring", RingMat("M_VfxDashRing", new Color(2.6f, 1.9f, 0.8f), false), 0.8f, 5f, 0.45f, 0.06f);

            var sparks = Sys("Gold Sparks", root.transform, Mat("M_VfxDashSpark", Sparkle, new Color(3f, 2.3f, 1.1f), false, 2.2f));
            var sm = sparks.main; sm.startLifetime = R(0.4f, 0.75f); sm.startSpeed = R(3f, 6.5f); sm.startSize = R(0.18f, 0.36f);
            sm.startColor = new ParticleSystem.MinMaxGradient(White, Gold);
            Burst(sparks, 14);
            Shape(sparks, ParticleSystemShapeType.Sphere, 0.5f);
            Drag(sparks, 3f); Spin(sparks, 160f);
            ColorLife(sparks, Grad(new[] { White, Amber }, 0f, 1f, 0.6f, 0.8f, 1f, 0f));
            Trails(sparks, Mat("M_VfxDashTrail", Trail, new Color(2.4f, 1.7f, 0.7f), false, 0.4f), 0.25f);
            return Finish(root);
        }

        // The golden star: the big celebration.
        public static GameObject Starburst()
        {
            Ensure();
            var root = Sys("VFX_Starburst", null, Mat("M_VfxStarSpark", Sparkle, new Color(3.2f, 2.4f, 1f), false, 2.5f));
            var m = root.main; m.duration = 0.4f;
            m.startLifetime = R(1f, 1.8f); m.startSpeed = R(4f, 10f); m.startSize = R(0.3f, 0.7f);
            m.startColor = new ParticleSystem.MinMaxGradient(White, Gold); m.gravityModifier = -0.15f;
            Burst(root, 60);
            Shape(root, ParticleSystemShapeType.Sphere, 0.6f);
            Vel(root, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(1f, 2.5f));
            Drag(root, 1.6f); Spin(root, 140f); Twinkle(root, 0.6f);
            SizeLife(root, Curve(0f, 0f, 0.06f, 1f, 0.6f, 0.7f, 1f, 0f));
            ColorLife(root, Grad(new[] { White, Gold, Amber }, 0f, 1f, 0.7f, 0.85f, 1f, 0f));
            Trails(root, Mat("M_VfxStarTrail", Trail, new Color(2.6f, 1.8f, 0.7f), false, 0.4f), 0.2f);

            Flash(root.transform, new Color(3.4f, 2.6f, 1.2f), 12f, 7f, 0.55f, "Star");
            WaterRing(root.transform, "Gold Shockwave", RingMat("M_VfxStarRing", new Color(3f, 2.2f, 0.9f), false), 1f, 15f, 1.1f, -0.5f);
            WaterRing(root.transform, "White Shockwave", RingMat("M_VfxStarRing2", new Color(2.4f, 2.4f, 2.6f), false), 0.8f, 9f, 0.8f, -0.45f, 0.12f);

            var rays = Sys("Light Rays", root.transform, Mat("M_VfxStarRay", Streak, new Color(2.8f, 2.1f, 1f), false, 1f), ParticleSystemRenderMode.Stretch);
            var rm = rays.main; rm.startLifetime = R(0.35f, 0.55f); rm.startSpeed = R(5f, 8f); rm.startSize = R(0.25f, 0.45f);
            Burst(rays, 16);
            Shape(rays, ParticleSystemShapeType.Sphere, 0.2f);
            var rr = rays.GetComponent<ParticleSystemRenderer>(); rr.velocityScale = 0.6f; rr.lengthScale = 3f;
            ColorLife(rays, Grad(new[] { White, Gold }, 0f, 0f, 0.1f, 1f, 1f, 0f));

            var embers = Sys("Rising Embers", root.transform, Mat("M_VfxStarEmber", Glow, new Color(2.6f, 1.6f, 0.6f), false, 3f));
            var em = embers.main; em.duration = 0.8f; em.startLifetime = R(1.6f, 2.6f); em.startSpeed = R(0.2f, 0.8f); em.startSize = R(0.08f, 0.16f);
            var emEm = embers.emission; emEm.rateOverTime = 50f;
            Shape(embers, ParticleSystemShapeType.Sphere, 2.2f);
            Vel(embers, Vector2.zero, new Vector2(1f, 2.6f), Vector2.zero);
            var noise = embers.noise; noise.enabled = true; noise.strength = 0.8f; noise.frequency = 0.6f; noise.scrollSpeed = 0.5f;
            ColorLife(embers, Grad(new[] { White, Gold, Amber }, 0f, 0f, 0.15f, 1f, 1f, 0f));
            return Finish(root);
        }

        // A near miss (also the hidden-orb reveal): a quick cool whoosh.
        public static GameObject NearMiss()
        {
            Ensure();
            var root = Sys("VFX_NearMiss", null, Mat("M_VfxMissSpark", Sparkle, new Color(1.2f, 2.6f, 3f), false, 2f));
            var m = root.main; m.duration = 0.2f;
            m.startLifetime = R(0.5f, 0.9f); m.startSpeed = R(2.5f, 5.5f); m.startSize = R(0.3f, 0.55f);
            Burst(root, 28);
            Shape(root, ParticleSystemShapeType.Circle, 0.5f, 0f, new Vector3(90f, 0f, 0f));
            Drag(root, 2f); Spin(root, 150f); Twinkle(root, 0.5f);
            SizeLife(root, Curve(0f, 0.2f, 0.1f, 1f, 1f, 0f));
            ColorLife(root, Grad(new[] { White, Cyan }, 0f, 1f, 0.6f, 0.8f, 1f, 0f));

            var whoosh = Sys("Whoosh", root.transform, Mat("M_VfxMissLine", Streak, new Color(1.6f, 2.6f, 3f), false, 1f), ParticleSystemRenderMode.Stretch);
            var wm = whoosh.main; wm.startLifetime = R(0.2f, 0.35f); wm.startSpeed = R(7f, 12f); wm.startSize = R(0.05f, 0.09f);
            Burst(whoosh, 14);
            Shape(whoosh, ParticleSystemShapeType.Circle, 0.3f, 0f, new Vector3(90f, 0f, 0f));
            var rr = whoosh.GetComponent<ParticleSystemRenderer>(); rr.velocityScale = 0.08f; rr.lengthScale = 2f;
            ColorLife(whoosh, Grad(new[] { White, Cyan }, 0f, 0f, 0.1f, 1f, 1f, 0f));

            Flash(root.transform, new Color(1.6f, 2.6f, 3f), 3.2f, 2.2f, 0.24f, "Miss");
            WaterRing(root.transform, "Ring", RingMat("M_VfxMissRing", new Color(1f, 2.4f, 2.8f)), 0.5f, 4.5f, 0.5f, -0.5f);
            Trails(root, Mat("M_VfxMissTrail", Trail, new Color(1f, 2f, 2.4f), false, 0.4f), 0.2f);
            return Finish(root);
        }

        // The whale crashing back into the sea: the biggest effect in the game.
        public static GameObject WhaleSplash()
        {
            Ensure();
            var root = Sys("VFX_WhaleSplash", null, Mat("M_VfxWhaleSpray", Droplet, new Color(0.8f, 0.92f, 1f, 0.95f), true), ParticleSystemRenderMode.Stretch);
            var m = root.main; m.duration = 0.5f; m.maxParticles = 600;
            m.startLifetime = R(1.2f, 2.2f); m.startSpeed = R(9f, 18f); m.startSize = R(0.18f, 0.45f); m.gravityModifier = 1.4f;
            Burst(root, 150); Burst(root, 70, 0.15f);
            Shape(root, ParticleSystemShapeType.Cone, 3f, 20f, new Vector3(-90f, 0f, 0f));
            var rr = root.GetComponent<ParticleSystemRenderer>(); rr.velocityScale = 0.06f; rr.lengthScale = 1.2f;
            ColorLife(root, Grad(new[] { White, White }, 0f, 1f, 0.7f, 0.85f, 1f, 0f));

            var crown = Sys("Crown", root.transform, Mat("M_VfxWhaleSpray", Droplet, new Color(0.8f, 0.92f, 1f, 0.95f), true), ParticleSystemRenderMode.Stretch);
            var cm = crown.main; cm.startLifetime = R(0.9f, 1.5f); cm.startSpeed = R(6f, 11f); cm.startSize = R(0.15f, 0.32f); cm.gravityModifier = 1.5f;
            Burst(crown, 90);
            Shape(crown, ParticleSystemShapeType.Cone, 3.5f, 68f, new Vector3(-90f, 0f, 0f));
            var cr = crown.GetComponent<ParticleSystemRenderer>(); cr.velocityScale = 0.06f; cr.lengthScale = 1.1f;
            ColorLife(crown, Grad(new[] { White, White }, 0f, 1f, 0.75f, 0.9f, 1f, 0f));

            var mist = Sys("Mist", root.transform, Mat("M_VfxWhaleMist", Mist, new Color(0.72f, 0.86f, 1f, 0.38f), true, 0f, 2f));
            var mm = mist.main; mm.startLifetime = R(2f, 3.4f); mm.startSpeed = R(1f, 4f); mm.startSize = R(5f, 10f);
            Burst(mist, 16); Burst(mist, 6, 0.3f);
            Shape(mist, ParticleSystemShapeType.Hemisphere, 3f);
            Spin(mist, 15f);
            var ts = mist.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 2; ts.numTilesY = 2; ts.frameOverTime = R(0f, 0.999f);
            SizeLife(mist, Curve(0f, 0.45f, 1f, 1.8f));
            ColorLife(mist, Grad(new[] { White, White }, 0f, 0f, 0.08f, 1f, 1f, 0f));

            WaterRing(root.transform, "Foam Ring", RingMat("M_VfxWhaleFoam", new Color(0.85f, 0.95f, 1f, 0.8f), true), 3f, 24f, 2.2f, 0.06f);
            WaterRing(root.transform, "Glow Ring", RingMat("M_VfxWhaleRing", new Color(0.4f, 2.2f, 2.6f), false), 2f, 30f, 1.6f, 0.08f, 0.1f);
            Flash(root.transform, new Color(1f, 2.4f, 2.8f), 20f, 14f, 0.5f, "Whale");

            var spread = Sys("Plankton Spread", root.transform, Mat("M_VfxWhalePlankton", Sparkle, new Color(0.5f, 2.4f, 2.8f), false, 2f));
            var sm = spread.main; sm.startLifetime = R(1.5f, 2.6f); sm.startSpeed = R(4f, 10f); sm.startSize = R(0.3f, 0.6f);
            Burst(spread, 70);
            Shape(spread, ParticleSystemShapeType.Circle, 3f, 0f, new Vector3(90f, 0f, 0f));
            spread.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            Drag(spread, 1.2f); Spin(spread, 60f); Twinkle(spread, 0.6f);
            ColorLife(spread, Grad(new[] { White, Cyan, Teal }, 0f, 1f, 0.6f, 0.8f, 1f, 0f));

            var column = Sys("Column Sparks", root.transform, Mat("M_VfxWhaleSpark", Sparkle, new Color(0.7f, 2.4f, 2.8f), false, 2.2f));
            var colm = column.main; colm.startLifetime = R(1f, 1.8f); colm.startSpeed = R(8f, 16f); colm.startSize = R(0.25f, 0.5f); colm.gravityModifier = 1.2f;
            Burst(column, 50);
            Shape(column, ParticleSystemShapeType.Cone, 2.5f, 22f, new Vector3(-90f, 0f, 0f));
            Spin(column, 100f);
            ColorLife(column, Grad(new[] { White, Cyan }, 0f, 1f, 0.7f, 0.85f, 1f, 0f));
            Trails(column, Mat("M_VfxWhaleTrail", Trail, new Color(0.4f, 1.8f, 2.2f), false, 0.4f), 0.15f);
            return Finish(root);
        }

        // ==================================================================
        // RESTYLING LOOPING / AMBIENT SYSTEMS (built by other steps)
        // ==================================================================

        // The glowing plankton the boat leaves behind: twinkling star sparks.
        public static void StyleWake(ParticleSystem ps)
        {
            Ensure();
            var m = ps.main;
            m.startSize = R(0.6f, 1.6f);    // BoatWake scales this with startSizeMultiplier
            m.startColor = new ParticleSystem.MinMaxGradient(Cyan, Mint);
            m.startRotation = R(0f, Mathf.PI * 2f);
            m.maxParticles = 500;
            Spin(ps, 70f); Twinkle(ps, 0.5f);
            SizeLife(ps, Curve(0f, 0f, 0.1f, 1f, 0.5f, 0.65f, 1f, 0f));
            ColorLife(ps, Grad(new[] { White, Cyan, Teal }, 0f, 1f, 0.6f, 0.8f, 1f, 0f));
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat("M_VfxWake", Sparkle, new Color(0.6f, 2.2f, 2.6f), false, 2f, 0.3f);
        }

        // Fireflies: warm glows with a white-hot core and short light trails.
        public static void StyleFireflies(ParticleSystem ps)
        {
            Ensure();
            var m = ps.main; m.startSize = R(0.14f, 0.28f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat("M_VfxFirefly", Glow, new Color(2.4f, 1.9f, 0.8f), false, 3.5f, 0.5f, 0.5f);
            Trails(ps, Mat("M_VfxFireflyTrail", Trail, new Color(1.6f, 1.2f, 0.4f), false, 0.3f), 0.12f, 0.6f);
            var t = ps.trails; t.ratio = 0.5f;
        }

        // Plankton motes drifting over the sea: tiny twinkling stars.
        public static void StylePlankton(ParticleSystem ps)
        {
            Ensure();
            var m = ps.main; m.startSize = R(0.12f, 0.3f); m.startRotation = R(0f, Mathf.PI * 2f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat("M_VfxPlanktonMote", Sparkle, new Color(0.6f, 2f, 2.4f), false, 2f, 0.4f);
            Twinkle(ps, 0.7f);
        }

        // Rain: soft pale streaks (matter, not light), plus ripples where it hits the sea.
        public static void StyleRain(ParticleSystem ps)
        {
            Ensure();
            var m = ps.main; m.startSize = R(0.035f, 0.07f); m.startColor = new Color(1f, 1f, 1f, 1f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat("M_VfxRain", Streak, new Color(0.72f, 0.82f, 1f, 0.4f), true, 0f, 0.3f, 2f);
            r.velocityScale = 0.05f; r.lengthScale = 3f;

            var old = ps.transform.Find("Rain Ripples");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var ripples = Sys("Rain Ripples", ps.transform, RingMat("M_VfxRainRipple", new Color(0.75f, 0.88f, 1f, 0.55f), true), ParticleSystemRenderMode.HorizontalBillboard);
            ripples.transform.localPosition = new Vector3(0f, -23f, -6f);   // rain sits ~12 m above the camera; this is the water
            var rm = ripples.main; rm.loop = true; rm.playOnAwake = true; rm.duration = 5f; rm.startLifetime = R(0.35f, 0.6f);
            rm.startSpeed = 0f; rm.startSize = R(0.5f, 1.1f); rm.maxParticles = 800;
            Shape(ripples, ParticleSystemShapeType.Box, 1f);
            var sh = ripples.shape; sh.scale = new Vector3(40f, 0.1f, 40f);
            SizeLife(ripples, Curve(0f, 0.15f, 1f, 1f));
            ColorLife(ripples, Grad(new[] { White, White }, 0f, 1f, 1f, 0f));
            ripples.gameObject.AddComponent<RainRipples>().rain = ps;
        }

        // The shooting star: a gradient light trail, an anamorphic head and falling stardust.
        public static void StyleShootingStar(GameObject star, TrailRenderer trail, Renderer head)
        {
            Ensure();
            trail.sharedMaterial = Mat("M_VfxShootingTrail", Trail, new Color(2.6f, 2.4f, 2.2f), false, 1f, 0.01f, 0.01f);
            trail.colorGradient = Grad(new[] { White, Gold, new Color(0.7f, 0.5f, 1f) }, 0f, 1f, 0.5f, 0.7f, 1f, 0f);
            trail.time = 0.7f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1.6f), new Keyframe(0.3f, 0.9f), new Keyframe(1f, 0f));
            head.sharedMaterial = Mat("M_VfxShootingHead", Flare, new Color(4f, 3.6f, 3f), false, 3f, 0.01f, 0.01f);
            head.transform.localScale = new Vector3(16f, 4f, 1f);

            var old = star.transform.Find("Stardust");
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var dust = Sys("Stardust", star.transform, Mat("M_VfxStardust", Sparkle, new Color(2.6f, 2.2f, 1.6f), false, 2f, 0.01f, 0.01f));
            var dm = dust.main; dm.loop = true; dm.playOnAwake = true; dm.startLifetime = R(0.6f, 1.2f); dm.startSpeed = 0f;
            dm.startSize = R(0.6f, 1.4f); dm.gravityModifier = 0.08f; dm.maxParticles = 300;
            var de = dust.emission; de.rateOverDistance = 0.35f;
            Spin(dust, 90f);
            SizeLife(dust, Curve(0f, 1f, 1f, 0f));
            ColorLife(dust, Grad(new[] { White, Gold }, 0f, 1f, 1f, 0f));
        }

        // The whale's spout: soft rising mist instead of round dots.
        public static void StyleSpout(ParticleSystem ps)
        {
            Ensure();
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Mat("M_VfxSpout", Mist, new Color(0.78f, 0.9f, 1f, 0.55f), true, 0f, 0.8f);
            var ts = ps.textureSheetAnimation; ts.enabled = true; ts.numTilesX = 2; ts.numTilesY = 2; ts.frameOverTime = R(0f, 0.999f);
            Spin(ps, 30f);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
