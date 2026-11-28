// SeaCreature: glowing sea creatures that behave the way the real animals do
// (Phase 9). Each one is an enemy with its own size, speed and light damage,
// and is dangerous only while it is actually in your way.
//
//   MantaGlide     a manta ray gliding across in a lazy S-curve; sometimes it
//                  breaches and belly-flops (mantas breach more than any other ray)
//   TurtlePaddle   a sea turtle swimming below the surface (harmless) that comes
//                  up to BREATHE for a few seconds (solid), then dives again.
//                  Bubbles rise just before it surfaces.
//   DolphinLeap    porpoising: long leaps alternated with swimming near the surface,
//                  the "top gear" of a travelling pod. Harmless mid-air, solid at
//                  the surface between leaps. Pods cross in a staggered line.
//   OrcaHunt       a hunting orca. STALK: only its tall dorsal fin slices along the
//                  lane edge (harmless). TURN: a splash as it turns in. CHARGE: it
//                  races across the lane at burst speed (~45 km/h), solid.
//                  It can also DIVE (wave-washing: the pod dives together and the
//                  wave rolls on without them).
//   HumpbackLunge  bubble-net feeding: a ring of bubbles glows on the water while
//                  the whale circles below (harmless), then it LUNGES straight up
//                  through the ring, a third of its body clearing the water (solid
//                  inside the ring), and falls back.
//   BowRide        dolphins riding alongside the boat (never solid, just company).
//
// The EncounterDirector spawns them and calls one of the Begin...() methods
// right after spawning. Fins, flukes and wings are animated in the shader
// (StarlitSeaCreature.shader).

using UnityEngine;

namespace StarlitTides
{
    public class SeaCreature : Obstacle
    {
        public enum Behaviour { MantaGlide, TurtlePaddle, DolphinLeap, OrcaHunt, HumpbackLunge, BowRide }

        [Header("Sea creature")]
        public Behaviour behaviour = Behaviour.MantaGlide;
        [Tooltip("Speed across the lane (m/s), picked at random in this range (when not set by an encounter).")]
        public Vector2 crossSpeed = new Vector2(1.2f, 2.2f);
        [Tooltip("How far it weaves forward/back while crossing (metres).")]
        public float weave = 1.5f;
        public float weaveFrequency = 0.35f;
        [Tooltip("Chance that a manta leaps once while crossing.")]
        [Range(0f, 1f)] public float leapChance = 0.4f;
        public float leapHeight = 2.6f;
        public float leapDuration = 1.3f;
        [Tooltip("Above this height (m over the water) the creature can't hit the boat.")]
        public float safeHeight = 1.1f;
        [Tooltip("Body length in metres (for depth and lunge maths).")]
        public float bodyLength = 3f;
        public Light glowLight;
        public Collider hitCollider;
        [Tooltip("Optional: bubbles/ring shown on the water as a warning (turtle breath, bubble net).")]
        public ParticleSystem telegraph;

        enum Phase { Swim, Submerged, Surfacing, Breathing, Stalk, Turn, Charge, Dive, Ring, Lunge, Fall, Ride, Leave }

        Phase phase;
        float phaseTime;
        float direction = 1f;    // +1 swims toward +X, -1 toward -X
        float speed;
        float holdTide;          // fraction of the tide it swims against (to time its crossing)
        float depth, targetDepth, depthRate = 2f;
        bool solidPhase = true;
        bool harmless;

        // leaps
        float leapStart = -100f, nextLeapTime;
        bool leapPlanned, porpoise, splashedOut;
        float porpoiseGap = 0.4f;
        float lastHeight;

        // per-behaviour settings
        float stalkTime, chargeSpeed, chargeHold, turnTime = 0.7f;
        float ringTime, breathTime, nextSurface;
        float rideSide, rideUntil, rideOffsetZ;

        float baseLight;
        Material materialInstance;

        protected override void Awake()
        {
            base.Awake();
            if (hitCollider == null) hitCollider = GetComponent<Collider>();
            if (glowLight != null) baseLight = glowLight.intensity;
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            // Default: swim toward (and past) the middle of the lane, timed so it
            // crosses your path about when the tide brings it to the boat.
            direction = transform.position.x > 0f ? -1f : 1f;
            float tide = Tide();
            float secondsToBoat = Mathf.Max(transform.position.z - 2f, 10f) / tide;
            float targetX = direction * Random.Range(0f, 9f);
            speed = Mathf.Clamp(Mathf.Abs(targetX - transform.position.x) / secondsToBoat, crossSpeed.x, crossSpeed.y);
            holdTide = 0f;
            extraDrift = new Vector3(direction * speed, 0f, 0f);
            transform.rotation = Heading(0f);
            leapStart = -100f;
            splashedOut = false;
            porpoise = false;
            harmless = false;
            depth = targetDepth = 0f;
            phase = Phase.Swim;
            phaseTime = 0f;
            lastHeight = 0f;
            if (telegraph != null) telegraph.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Offset the fin animation so two creatures never flap in sync.
            Renderer r = visual != null ? visual.GetComponentInChildren<Renderer>() : null;
            if (r != null)
            {
                if (materialInstance == null) materialInstance = r.material;
                materialInstance.SetFloat("_AnimTime", Seed);
            }

            leapPlanned = behaviour == Behaviour.MantaGlide && Random.value < leapChance;
            nextLeapTime = Time.time + Random.Range(2.5f, 6f);
            if (behaviour == Behaviour.DolphinLeap) { porpoise = true; nextLeapTime = Time.time + Random.Range(0.2f, 1.2f); }
            if (behaviour == Behaviour.TurtlePaddle) { phase = Phase.Submerged; nextSurface = Random.Range(1.5f, 3f); breathTime = 2.6f; depth = targetDepth = -1.5f; }
        }

        void OnDestroy()
        {
            if (materialInstance != null) Destroy(materialInstance);
        }

        static float Tide() { return OceanController.Instance != null ? Mathf.Max(OceanController.Instance.tideSpeed, 3f) : 8f; }

        Quaternion Heading(float weaveTurn)
        {
            float yaw = direction > 0f ? 90f : -90f;
            return Quaternion.Euler(0f, yaw + weaveTurn, 0f);
        }

        // ------------------------------------------------------------------ set-up by encounters

        /// <summary>Swim across the lane: direction (+1/-1), speed, and the fraction of the tide it swims against.</summary>
        public void BeginCrossing(float dir, float crossingSpeed, float hold = 0f)
        {
            direction = Mathf.Sign(dir);
            speed = crossingSpeed;
            holdTide = Mathf.Clamp01(hold);
            transform.rotation = Heading(0f);
        }

        /// <summary>Dolphin pods: rhythmic leaps, offset by 'delay' seconds so the pod ripples.</summary>
        public void BeginPorpoising(float delay, float gap = 0.4f)
        {
            porpoise = true;
            porpoiseGap = gap;
            nextLeapTime = Time.time + delay;
        }

        /// <summary>Turtles: first breath after 'delay' seconds.</summary>
        public void BeginSurfacing(float delay)
        {
            phase = Phase.Submerged; phaseTime = 0f;
            nextSurface = delay;
            depth = targetDepth = -1.5f;
        }

        /// <summary>Orcas: stalk (fin only) for 'stalk' seconds, then charge across at 'charge' m/s, crossing x=0 near 'targetZ'.</summary>
        public void BeginHunt(float dir, float stalk, float charge, float targetZ)
        {
            direction = Mathf.Sign(dir);
            stalkTime = stalk; chargeSpeed = charge;
            phase = Phase.Stalk; phaseTime = 0f;
            // It rises from below until only the dorsal fin breaks the surface.
            depth = -bodyLength * 0.4f;
            targetDepth = StalkDepth();
            depthRate = 1.2f;
            // Ride along with the boat while stalking (hunting orcas pace their prey).
            holdTide = 1f;
            speed = 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, 0f);   // facing forward, along the lane

            // Charge timing: cross the lane centre about 'targetZ' ahead of the boat row.
            float tide = Tide();
            float travel = Mathf.Abs(transform.position.x) + 1f;
            float chargeSeconds = travel / Mathf.Max(1f, charge);
            float approach = Mathf.Max(0f, transform.position.z - targetZ);   // metres it must close while charging
            chargeHold = Mathf.Clamp01(1f - approach / Mathf.Max(1f, tide * (chargeSeconds + turnTime)));
        }

        /// <summary>Orcas: the stalking pod dives (wave-washing).</summary>
        public void BeginDive()
        {
            phase = Phase.Dive; phaseTime = 0f;
            targetDepth = -bodyLength * 0.8f; depthRate = 2.5f;
            holdTide = 0f; speed = 0f;
            Splash(transform.position, 1.2f);
        }

        /// <summary>Humpbacks: bubble net for 'ring' seconds, then the lunge.</summary>
        public void BeginLunge(float ring)
        {
            ringTime = ring;
            phase = Phase.Ring; phaseTime = 0f;
            speed = 0f; holdTide = 0f;
            depth = targetDepth = -bodyLength;   // deep below the ring
            transform.rotation = Quaternion.Euler(-85f, Random.Range(0f, 360f), 0f);
            if (telegraph != null) telegraph.Play(true);
            if (AudioManager.Instance != null) AudioManager.Instance.PlayCue(AudioManager.SeaCue.BubbleNet, transform.position);   // bubbles rising
        }

        /// <summary>Dolphins: ride beside the boat on 'side' (+1/-1) for 'duration' seconds.</summary>
        public void BeginBowRide(float side, float duration, float offsetZ)
        {
            harmless = true;
            rideSide = Mathf.Sign(side);
            rideUntil = Time.time + duration;
            rideOffsetZ = offsetZ;
            phase = Phase.Ride; phaseTime = 0f;
            porpoise = true; porpoiseGap = Random.Range(0.6f, 1.2f);
            speed = 0f; holdTide = 1f;
            transform.rotation = Quaternion.identity;
        }

        float StalkDepth()
        {
            // The dorsal fin of an orca is ~1.8 m tall: keep the back ~0.9 m under.
            return -bodyLength * 0.13f;
        }

        // ------------------------------------------------------------------ every frame

        protected override bool TracksApproach { get { return solidPhase && !harmless; } }

        protected override void Tick(float dt)
        {
            base.Tick(dt);
            phaseTime += dt;
            float age = Time.time - SpawnTime;
            float tide = Tide();
            Vector3 position = transform.position;
            float pitch = 0f, roll = 0f, weaveTurn = 0f;
            Quaternion? overrideRotation = null;
            solidPhase = true;

            switch (phase)
            {
                case Phase.Swim:
                    // Weave forward and back while crossing (an S-curve on the water).
                    position.z += Mathf.Cos(age * weaveFrequency * Mathf.PI * 2f + Seed) * weave * weaveFrequency * Mathf.PI * 2f * dt;
                    weaveTurn = -direction * Mathf.Cos(age * weaveFrequency * Mathf.PI * 2f + Seed) * 20f * Mathf.Clamp01(weave);
                    break;

                case Phase.Submerged:   // turtle below the surface
                    solidPhase = false;
                    targetDepth = -1.5f;
                    if (phaseTime > nextSurface - 0.9f && telegraph != null && !telegraph.isPlaying) telegraph.Play(true);
                    if (phaseTime >= nextSurface) { phase = Phase.Surfacing; phaseTime = 0f; targetDepth = 0f; depthRate = 2.2f; }
                    break;
                case Phase.Surfacing:
                    solidPhase = depth > -0.6f;
                    if (depth > -0.05f)
                    {
                        phase = Phase.Breathing; phaseTime = 0f; Splash(position, 0.5f); if (telegraph != null) telegraph.Stop();
                        if (AudioManager.Instance != null) AudioManager.Instance.PlayCue(AudioManager.SeaCue.Breath, position);   // a soft breath
                    }
                    break;
                case Phase.Breathing:
                    pitch = Mathf.Sin(age * 1.7f + Seed) * 5f - 6f;   // head up to breathe
                    if (phaseTime >= breathTime) { phase = Phase.Submerged; phaseTime = 0f; nextSurface = Random.Range(3f, 5f); targetDepth = -1.5f; depthRate = 1f; }
                    break;

                case Phase.Stalk:       // orca: fin only, pacing the boat at the lane edge
                    solidPhase = false;
                    position.z += Mathf.Sin(age * 0.9f + Seed) * 0.6f * dt;
                    overrideRotation = Quaternion.Euler(0f, Mathf.Sin(age * 0.6f + Seed) * 6f, 0f);
                    if (phaseTime >= stalkTime) { phase = Phase.Turn; phaseTime = 0f; Splash(position, 1f); }
                    break;
                case Phase.Turn:
                    solidPhase = false;
                    {
                        float t = Mathf.Clamp01(phaseTime / turnTime);
                        overrideRotation = Quaternion.Slerp(Quaternion.identity, Heading(0f), t * t * (3f - 2f * t));
                        targetDepth = -bodyLength * 0.015f; depthRate = 1.5f;   // the back breaks the surface as it charges
                        holdTide = Mathf.Lerp(1f, chargeHold, t);
                        speed = chargeSpeed * t * 0.6f;
                        if (phaseTime >= turnTime) { phase = Phase.Charge; phaseTime = 0f; }
                    }
                    break;
                case Phase.Charge:
                    speed = Mathf.MoveTowards(speed, chargeSpeed, chargeSpeed * 2f * dt);
                    holdTide = chargeHold;
                    // a powerful, shallow porpoise every so often
                    if (phaseTime > 0.4f && Time.time - leapStart > 1.6f && Random.value < dt * 0.8f) { leapStart = Time.time; splashedOut = false; }
                    break;
                case Phase.Dive:
                    solidPhase = false;
                    if (phaseTime > 4f) Despawn();
                    break;

                case Phase.Ring:        // humpback circling below its bubble net
                    solidPhase = false;
                    if (phaseTime >= ringTime)
                    {
                        phase = Phase.Lunge; phaseTime = 0f;
                        targetDepth = bodyLength * 0.33f - bodyLength * 0.5f;   // a third of the body clears the water
                        depthRate = bodyLength * 0.9f;
                        Splash(position, 2.2f);
                        if (AudioManager.Instance != null) AudioManager.Instance.PlayCue(AudioManager.SeaCue.WhaleSong, position, 0.9f);
                        if (telegraph != null) telegraph.Stop();
                    }
                    break;
                case Phase.Lunge:
                    solidPhase = depth > -bodyLength * 0.6f;
                    if (phaseTime > 1.6f) { phase = Phase.Fall; phaseTime = 0f; targetDepth = -bodyLength * 1.1f; depthRate = bodyLength * 0.45f; }
                    break;
                case Phase.Fall:
                    solidPhase = depth > -bodyLength * 0.55f;
                    {
                        float t = Mathf.Clamp01(phaseTime / 1.6f);
                        Vector3 e = transform.rotation.eulerAngles;
                        overrideRotation = Quaternion.Euler(Mathf.Lerp(-85f, -40f, t), e.y, Mathf.Lerp(0f, 35f, t));
                        if (phaseTime > 0.5f && phaseTime - dt <= 0.5f) Splash(position, 2.6f);
                        if (phaseTime > 4f) Despawn();
                    }
                    break;

                case Phase.Ride:        // bow-riding next to the boat
                    solidPhase = false;
                    {
                        BoatController boat = BoatController.Instance;
                        if (boat != null)
                        {
                            Vector3 b = boat.transform.position;
                            float wantX = b.x + rideSide * (3.6f + Mathf.Sin(age * 0.7f + Seed) * 0.8f);
                            float wantZ = b.z + rideOffsetZ + Mathf.Sin(age * 0.5f + Seed * 2f) * 1.5f;
                            position.x = Mathf.Lerp(position.x, wantX, 1f - Mathf.Exp(-2.5f * dt));
                            position.z = Mathf.Lerp(position.z, wantZ, 1f - Mathf.Exp(-1.5f * dt));
                        }
                        if (Time.time > rideUntil) { phase = Phase.Leave; phaseTime = 0f; holdTide = 0.3f; porpoise = false; }
                    }
                    break;
                case Phase.Leave:
                    solidPhase = false;
                    targetDepth = -2.5f; depthRate = 1.2f;
                    break;
            }

            // Movement across the lane, and swimming against the tide to keep pace.
            extraDrift = new Vector3(direction * speed, 0f, tide * holdTide);
            if (phase == Phase.Ride) extraDrift = new Vector3(0f, 0f, tide);   // (position is steered directly)

            // ---- leaps (mantas once, dolphins rhythmically, charging orcas now and then) ----
            if (porpoise && Time.time >= nextLeapTime && Time.time - leapStart > leapDuration)
            {
                leapStart = Time.time;
                splashedOut = false;
                nextLeapTime = Time.time + leapDuration + porpoiseGap * Random.Range(0.8f, 1.25f);
            }
            else if (leapPlanned && Time.time >= nextLeapTime && Time.time - leapStart > leapDuration)
            {
                leapStart = Time.time;
                splashedOut = false;
                leapPlanned = false;   // a manta breaches once
            }

            float height = 0f;
            float duration = phase == Phase.Charge ? 0.9f : leapDuration;
            float lift = phase == Phase.Charge ? 0.9f : leapHeight;
            float lt = (Time.time - leapStart) / duration;
            if (lt >= 0f && lt <= 1f)
            {
                height = lift * 4f * lt * (1f - lt);                       // a parabola
                pitch = Mathf.Lerp(35f, -35f, lt);                          // nose up, then nose down
                if (behaviour == Behaviour.MantaGlide) roll = Mathf.Sin(lt * Mathf.PI) * 25f * direction;
                if (!splashedOut && lt > 0.05f) { splashedOut = true; Splash(position, phase == Phase.Ride ? 0.4f : 0.7f); }
            }
            else if (lastHeight > 0.3f)
            {
                Splash(position, 0.9f);   // back into the water
            }
            lastHeight = height;

            depth = Mathf.MoveTowards(depth, targetDepth, depthRate * dt);
            position.y += height + depth;
            transform.position = position;
            transform.rotation = overrideRotation.HasValue
                ? overrideRotation.Value * Quaternion.Euler(-pitch, 0f, roll)
                : (phase == Phase.Ride || phase == Phase.Leave ? Quaternion.Euler(-pitch, Mathf.Sin(age + Seed) * 4f, 0f) : Heading(weaveTurn) * Quaternion.Euler(-pitch, 0f, roll));
            if (phase == Phase.Lunge || phase == Phase.Ring)
            {
                Vector3 e = transform.rotation.eulerAngles;
                transform.rotation = Quaternion.Euler(-85f, e.y, 0f);
            }

            // Solid only when it's really in your way: at the surface, low, and not harmless.
            bool solid = !harmless && solidPhase && height < safeHeight;
            if (hitCollider != null) hitCollider.enabled = solid;

            // The warning (bubbles / bubble ring) sits on the water above the creature.
            if (telegraph != null)
            {
                float water = OceanController.Instance != null ? OceanController.Instance.GetWaterHeight(position) : 0f;
                telegraph.transform.SetPositionAndRotation(new Vector3(position.x, water + 0.05f, position.z), Quaternion.identity);
            }

            if (glowLight != null)
                glowLight.intensity = baseLight * (0.8f + 0.2f * Mathf.Sin(Time.time * 1.6f + Seed)) * (depth < -2f ? 0.5f : 1f);
        }

        void Splash(Vector3 at, float size)
        {
            Vector3 water = new Vector3(at.x, OceanController.Instance != null ? OceanController.Instance.GetWaterHeight(at) : 0f, at.z);
            // A soft splash sound sized to the animal and the moment (dolphin leap ... whale lunge).
            AudioManager audio = AudioManager.Instance;
            if (audio != null)
            {
                float big = size * (bodyLength >= 10f ? 1.4f : bodyLength >= 5f ? 1.1f : 0.8f);
                audio.PlayCue(big >= 1.8f ? AudioManager.SeaCue.BigSplash : big >= 0.85f ? AudioManager.SeaCue.MediumSplash : AudioManager.SeaCue.SmallSplash, water);
            }
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, water);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(water, 2.5f * size, 2f, 1.4f);
        }
    }
}
