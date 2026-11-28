// BONUS FEATURE 10 - Adaptive Audio
//
// Music and ambience react to the game:
// - Two music layers play in perfect sync: a calm pad and an intense arpeggio.
//   The intense layer fades in with speed (difficulty), combo, storms and Starburst.
// - Ambience: waves always, wind gets stronger in storms, rain only in storms.
// - Sound effects for orbs (the chime climbs a musical scale as your combo
//   grows), dashing, crashing, near misses, Starburst, thunder (a moment after
//   each lightning flash), the whale, and game over.
//
// Phase 9: the ambience and music are stereo and rebuilt to be calm (see
// ProceduralAudio). Rain plays quieter, and a soft chime marks each new zone.
//
// No audio files needed: if a clip slot below is empty, the sound is generated
// by ProceduralAudio on a background thread when the game starts. Drag in your
// own clips (e.g. from Pixabay) to replace any of them.

using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace StarlitTides
{
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("Volumes")]
        [Range(0f, 1f)] public float musicVolume = 0.55f;
        [Range(0f, 1f)] public float ambienceVolume = 0.6f;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;

        [Header("Optional: your own clips (empty = generated)")]
        public AudioClip musicCalm;
        public AudioClip musicIntense;
        public AudioClip waves;
        public AudioClip wind;
        public AudioClip rain;
        public AudioClip orbChime;
        public AudioClip dash;
        public AudioClip hit;
        public AudioClip nearMiss;
        public AudioClip starburst;
        public AudioClip thunder;
        public AudioClip whaleCall;
        public AudioClip splash;
        public AudioClip gameOver;
        public AudioClip uiClick;
        public AudioClip zoneChime;

        public enum SeaCue { SmallSplash, MediumSplash, BigSplash, WhaleSong, BubbleNet, OrcaCall, DolphinCall, WaveSweep, Breath }
        AudioClip boatWater, splashSmall, splashMedium, splashBig, bubbleNet, waveSweep, orcaCall, dolphinCall, breath, whaleSoft;
        float lastMediumSplash = -10f, lastBigSplash = -10f;
        AudioSource boatSource;
        AudioLowPassFilter boatFilter;
        AudioSource[] seaSources;
        int nextSea;
        float lastSmallSplash = -10f;

        // Semitone steps of a pentatonic scale: the orb chime climbs these as the combo grows.
        static readonly int[] ComboScale = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21, 24 };

        AudioSource calmSource, intenseSource, wavesSource, windSource, rainSource;
        AudioSource[] sfxSources;
        int nextSfx;
        float intensity;
        bool ready;

        // Generated sample data, filled in on a background thread.
        class Generated
        {
            public SoundData calm, intense, waves, wind, rain;
            public float[] orb, dash, hit, nearMiss, starburst, thunder, whale, splash, gameOver, click, zone;
            public SoundData boatWater;
            public float[] splashSmall, splashMedium, splashBig, bubbleNet, waveSweep, orcaCall, dolphinCall, breath, whaleSoft;
        }
        Task<Generated> generating;

        void Awake()
        {
            Instance = this;
            calmSource = MakeSource("Music Calm", true);
            intenseSource = MakeSource("Music Intense", true);
            wavesSource = MakeSource("Ambience Waves", true);
            windSource = MakeSource("Ambience Wind", true);
            rainSource = MakeSource("Ambience Rain", true);
            sfxSources = new AudioSource[8];
            for (int i = 0; i < sfxSources.Length; i++) sfxSources[i] = MakeSource("SFX " + i, false);
            // Phase 9: the boat's water loop (brighter with speed) and placed sea sounds.
            boatSource = MakeSource("Boat Water", true);
            boatFilter = boatSource.gameObject.AddComponent<AudioLowPassFilter>();
            boatFilter.cutoffFrequency = 1200f;
            seaSources = new AudioSource[6];
            for (int i = 0; i < seaSources.Length; i++) seaSources[i] = MakeSource("Sea " + i, false);

            // Pure maths only, so it's safe to run off the main thread (no hitch at startup).
            generating = Task.Run(() => GenerateAll());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        AudioSource MakeSource(string name, bool loop)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.loop = loop;
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 2D sound
            source.volume = 0f;
            return source;
        }

        static Generated GenerateAll()
        {
            Generated g = new Generated();
            g.calm = ProceduralAudio.MusicCalm(11);
            g.intense = ProceduralAudio.MusicIntense(12);
            g.waves = ProceduralAudio.Waves(13);
            g.wind = ProceduralAudio.Wind(14);
            g.rain = ProceduralAudio.Rain(15);
            g.orb = ProceduralAudio.OrbChime();
            g.dash = ProceduralAudio.Whoosh(16);
            g.hit = ProceduralAudio.Thud(17);
            g.nearMiss = ProceduralAudio.Shimmer();
            g.starburst = ProceduralAudio.StarburstSwell();
            g.thunder = ProceduralAudio.Thunder(18);
            g.whale = ProceduralAudio.WhaleCall();
            g.splash = ProceduralAudio.Splash(19);
            g.gameOver = ProceduralAudio.GameOverTone();
            g.click = ProceduralAudio.UiClick();
            g.zone = ProceduralAudio.ZoneChime();
            g.boatWater = ProceduralAudio.BoatWater(21);
            g.splashSmall = ProceduralAudio.SoftSplash(22, 0.2f);
            g.splashMedium = ProceduralAudio.SoftSplash(23, 0.55f);
            g.splashBig = ProceduralAudio.SoftSplash(24, 1f);
            g.bubbleNet = ProceduralAudio.BubbleNet(25);
            g.waveSweep = ProceduralAudio.WaveSweep(26);
            g.orcaCall = ProceduralAudio.Whistle(27, 720f, 2);
            g.dolphinCall = ProceduralAudio.Whistle(28, 1350f, 3);
            g.breath = ProceduralAudio.Breath(29);
            g.whaleSoft = ProceduralAudio.WhaleSongSoft();
            return g;
        }

        static AudioClip ToClip(string name, float[] samples)
        {
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, ProceduralAudio.SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        static AudioClip ToClip(string name, SoundData sound)
        {
            int frames = sound.samples.Length / sound.channels;
            AudioClip clip = AudioClip.Create(name, frames, sound.channels, ProceduralAudio.SampleRate, false);
            clip.SetData(sound.samples, 0);
            return clip;
        }

        // Called once the background thread has finished.
        void FinishSetup(Generated g)
        {
            // Use the generated sound only where no clip was assigned in the Inspector.
            if (musicCalm == null) musicCalm = ToClip("Music Calm", g.calm);
            if (musicIntense == null) musicIntense = ToClip("Music Intense", g.intense);
            if (waves == null) waves = ToClip("Waves", g.waves);
            if (wind == null) wind = ToClip("Wind", g.wind);
            if (rain == null) rain = ToClip("Rain", g.rain);
            if (orbChime == null) orbChime = ToClip("Orb Chime", g.orb);
            if (dash == null) dash = ToClip("Dash", g.dash);
            if (hit == null) hit = ToClip("Hit", g.hit);
            if (nearMiss == null) nearMiss = ToClip("Near Miss", g.nearMiss);
            if (starburst == null) starburst = ToClip("Starburst", g.starburst);
            if (thunder == null) thunder = ToClip("Thunder", g.thunder);
            if (whaleCall == null) whaleCall = ToClip("Whale Call", g.whale);
            if (splash == null) splash = ToClip("Splash", g.splash);
            if (gameOver == null) gameOver = ToClip("Game Over", g.gameOver);
            if (uiClick == null) uiClick = ToClip("UI Click", g.click);
            if (zoneChime == null) zoneChime = ToClip("Zone Chime", g.zone);
            boatWater = ToClip("Boat Water", g.boatWater);
            splashSmall = ToClip("Splash Small", g.splashSmall);
            splashMedium = ToClip("Splash Medium", g.splashMedium);
            splashBig = ToClip("Splash Big", g.splashBig);
            bubbleNet = ToClip("Bubble Net", g.bubbleNet);
            waveSweep = ToClip("Wave Sweep", g.waveSweep);
            orcaCall = ToClip("Orca Call", g.orcaCall);
            dolphinCall = ToClip("Dolphin Call", g.dolphinCall);
            breath = ToClip("Breath", g.breath);
            whaleSoft = ToClip("Whale Song (soft)", g.whaleSoft);
            // The giant whale's splash-down uses the new soft, deep splash too.
            splash = splashBig;
            boatSource.clip = boatWater;
            boatSource.Play();

            // The raw sample arrays are no longer needed once the clips exist: let them go.
            generating = null;

            calmSource.clip = musicCalm;
            intenseSource.clip = musicIntense;
            wavesSource.clip = waves;
            windSource.clip = wind;
            rainSource.clip = rain;

            // Start both music layers at exactly the same moment so they stay in sync.
            double startTime = AudioSettings.dspTime + 0.2;
            calmSource.PlayScheduled(startTime);
            intenseSource.PlayScheduled(startTime);
            wavesSource.Play();
            windSource.Play();
            rainSource.Play();
            ready = true;
        }

        void OnEnable()
        {
            Orb.OnCollected += HandleOrb;
            BoatController.OnDash += HandleDash;
            BoatController.OnHit += HandleHit;
            NearMissDetector.OnNearMiss += HandleNearMiss;
            PowerUpManager.OnStarburstStarted += HandleStarburst;
            WeatherManager.OnLightning += HandleLightning;
            WhaleEvent.OnBreach += HandleWhale;
            WhaleEvent.OnSplashDown += HandleSplash;
            GameManager.OnGameOver += HandleGameOver;
            VoyageDirector.OnZoneStarted += HandleZone;
        }

        void OnDisable()
        {
            Orb.OnCollected -= HandleOrb;
            BoatController.OnDash -= HandleDash;
            BoatController.OnHit -= HandleHit;
            NearMissDetector.OnNearMiss -= HandleNearMiss;
            PowerUpManager.OnStarburstStarted -= HandleStarburst;
            WeatherManager.OnLightning -= HandleLightning;
            WhaleEvent.OnBreach -= HandleWhale;
            WhaleEvent.OnSplashDown -= HandleSplash;
            GameManager.OnGameOver -= HandleGameOver;
            VoyageDirector.OnZoneStarted -= HandleZone;
        }

        void Update()
        {
            if (!ready)
            {
                if (generating != null && generating.IsCompleted) FinishSetup(generating.Result);
                return;
            }
            UpdateMix();
        }

        // How "exciting" the moment is (0..1) decides how much intense music plays.
        void UpdateMix()
        {
            float target = 0f;
            if (DifficultyManager.Instance != null) target += DifficultyManager.Instance.Level * 0.45f;
            if (ComboSystem.Instance != null) target += Mathf.Clamp01(ComboSystem.Instance.Combo / 20f) * 0.35f;
            float storm = SkyController.Instance != null ? SkyController.Instance.storm : 0f;
            target += storm * 0.3f;
            if (PowerUpManager.Instance != null && PowerUpManager.Instance.StarburstActive) target += 0.4f;

            GameManager game = GameManager.Instance;
            bool playing = game != null && game.IsPlaying;
            if (!playing) target = 0f;   // menus, intro and game over stay calm
            target = Mathf.Clamp01(target);

            intensity = Mathf.MoveTowards(intensity, target, Time.unscaledDeltaTime * 0.25f);   // slow, musical fades

            calmSource.volume = musicVolume * (0.8f - intensity * 0.3f);
            intenseSource.volume = musicVolume * intensity;

            // The boat through the water: louder, brighter and a touch higher as it speeds up;
            // a hard turn adds a soft rush of spray. Quiet on the menu, fades out at game over.
            BoatController boat = BoatController.Instance;
            float tideNow = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 3f;
            float speed01 = Mathf.Clamp01((tideNow - 3f) / 19f);
            float turn = boat != null ? Mathf.Clamp01((Mathf.Abs(boat.Velocity.x) - 4f) / 12f) : 0f;
            bool over = game != null && game.State == GameState.GameOver;
            // (A quiet bed under the music: players found it too loud and noisy.)
            float boatTarget = over ? 0f : sfxVolume * (playing ? 0.05f + 0.11f * speed01 + 0.05f * turn : 0.03f);
            boatSource.volume = Mathf.MoveTowards(boatSource.volume, boatTarget, Time.unscaledDeltaTime * 0.6f);
            boatSource.pitch = 0.95f + 0.1f * speed01;
            boatFilter.cutoffFrequency = Mathf.Lerp(boatFilter.cutoffFrequency, 450f + 900f * Mathf.Max(speed01, turn), 1f - Mathf.Exp(-2f * Time.unscaledDeltaTime));

            float waveScale = OceanController.Instance != null ? OceanController.Instance.waveScale : 1f;
            wavesSource.volume = ambienceVolume * Mathf.Clamp01(0.5f + waveScale * 0.2f);
            windSource.volume = ambienceVolume * (0.1f + storm * 0.4f);
            rainSource.volume = ambienceVolume * storm * storm * 0.6f;
        }

        /// <summary>
        /// A sea sound at a place in the world: quieter with distance from the boat, and
        /// panned left/right by where it is. Small splashes are rate-limited so a whole
        /// porpoising pod stays gentle.
        /// </summary>
        public void PlayCue(SeaCue cue, Vector3 at, float intensity = 1f)
        {
            if (!ready) return;
            AudioClip clip = null; float vol = 0.6f;
            switch (cue)
            {
                // Quiet and rare, so they sit in the scene instead of on top of it.
                case SeaCue.SmallSplash:
                    if (Time.time - lastSmallSplash < 0.6f) return;
                    lastSmallSplash = Time.time; clip = splashSmall; vol = 0.1f; break;
                case SeaCue.MediumSplash:
                    if (Time.time - lastMediumSplash < 0.5f) return;
                    lastMediumSplash = Time.time; clip = splashMedium; vol = 0.15f; break;
                case SeaCue.BigSplash:
                    if (Time.time - lastBigSplash < 0.4f) return;
                    lastBigSplash = Time.time; clip = splashBig; vol = 0.22f; break;
                case SeaCue.WhaleSong: clip = whaleSoft; vol = 0.2f; break;
                case SeaCue.BubbleNet: clip = bubbleNet; vol = 0.16f; break;
                case SeaCue.WaveSweep: clip = waveSweep; vol = 0.14f; break;
                // (Orca and dolphin whistles and the turtle breath were removed: they felt out of place.)
                case SeaCue.OrcaCall: case SeaCue.DolphinCall: case SeaCue.Breath: return;
            }
            if (clip == null) return;
            Vector3 listener = BoatController.Instance != null ? BoatController.Instance.transform.position : Vector3.zero;
            Vector3 offset = at - listener; offset.y = 0f;
            float atten = 1f / (1f + offset.magnitude / 16f);   // far-off splashes fade to almost nothing
            AudioSource source = seaSources[nextSea];
            nextSea = (nextSea + 1) % seaSources.Length;
            source.panStereo = Mathf.Clamp(offset.x / 40f, -0.5f, 0.5f);   // gentle placement, never hard left/right
            source.pitch = Random.Range(0.94f, 1.04f);
            source.PlayOneShot(clip, vol * intensity * atten * sfxVolume);
        }

        public void PlaySfx(AudioClip clip, float volume, float pitch = 1f)
        {
            if (clip == null || !ready) return;
            AudioSource source = sfxSources[nextSfx];
            nextSfx = (nextSfx + 1) % sfxSources.Length;   // take turns so sounds don't cut each other off
            source.pitch = pitch;
            source.PlayOneShot(clip, volume * sfxVolume);
        }

        void HandleOrb(Orb orb)
        {
            int combo = ComboSystem.Instance != null ? ComboSystem.Instance.Combo : 0;
            int step = ComboScale[Mathf.Clamp(combo, 0, ComboScale.Length - 1)];
            PlaySfx(orbChime, 0.32f, Mathf.Pow(2f, step / 12f));   // orbs come about once a second: keep it a soft touch
        }

        // Menu buttons call this.
        public void PlayClick() { PlaySfx(uiClick, 0.5f, Random.Range(0.97f, 1.03f)); }

        void HandleDash() { PlaySfx(dash, 0.35f, Random.Range(0.95f, 1.05f)); }
        void HandleHit(Obstacle obstacle) { PlaySfx(hit, 0.7f, Random.Range(0.9f, 1.05f)); }
        void HandleNearMiss(int points) { PlaySfx(nearMiss, 0.6f); }
        void HandleStarburst() { PlaySfx(starburst, 0.8f); }
        void HandleWhale() { PlaySfx(whaleSoft != null ? whaleSoft : whaleCall, 0.4f); }   // (soft song, not the old wobbly call)
        void HandleSplash() { PlaySfx(splash, 0.4f); }
        void HandleGameOver() { PlaySfx(gameOver, 0.7f); }
        void HandleZone(VoyageDirector.Zone zone, int voyage)
        {
            if (VoyageDirector.Instance != null && VoyageDirector.Instance.Stage == 0) return;
            PlaySfx(zoneChime, 0.55f);
        }

        void HandleLightning(float strength)
        {
            StartCoroutine(ThunderAfterDelay(strength));
        }

        // Light travels faster than sound: thunder comes a moment after the flash.
        IEnumerator ThunderAfterDelay(float strength)
        {
            yield return new WaitForSeconds(Random.Range(0.3f, 1.4f));
            PlaySfx(thunder, 0.4f + strength * 0.3f, Random.Range(0.85f, 1.0f));
        }
    }
}
