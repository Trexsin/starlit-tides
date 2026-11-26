/*
 * ============================================================================
 *   STARLIT TIDES
 *   A bioluminescent night-sea arcade game  -  C++ / OpenGL / GLUT
 * ============================================================================
 *   Guide a small glowing paper boat across a dark night sea. The boat's light
 *   slowly fades; collect glowing plankton orbs to keep it alive. Dodge rocks
 *   and jellyfish. If the light runs out, darkness takes over.
 *
 *   Controls:  Arrow keys / WASD  move        Space   dash
 *              V  switch 2D / 3D view          P       pause
 *              Enter  start / restart          Esc     quit
 *
 *   Graphics concepts (search these tags to find them in the code):
 *     [TRANSFORM]   2D and 3D translate / rotate / scale with the matrix stack
 *     [PROJECTION]  gluOrtho2D (2D view, HUD) and gluPerspective (3D view)
 *     [INTERP]      per-vertex colour interpolation (gradients, palettes)
 *     [BLEND]       alpha blending (darkness, flashes) and additive glow
 *     [PARTICLES]   sparkle / splash / wake particle system
 *     [COLLISION]   circle-circle collision and near-miss detection
 *     [TIMER]       glutTimerFunc animation driven by delta time
 *     [MIDPOINT]    midpoint circle algorithm (written by hand)
 *     [DDA]         DDA line algorithm (written by hand)
 *     [FOG]         OpenGL fog for depth in the 3D view
 * ============================================================================
 */

#ifdef _WIN32
#  ifndef NOMINMAX
#    define NOMINMAX
#  endif
#  include <windows.h>
#endif

#define GL_SILENCE_DEPRECATION
#ifdef __APPLE__
#  include <GLUT/glut.h>
#else
#  include <GL/glut.h>
#endif

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <ctime>
#include <deque>
#include <string>
#include <vector>

/* ============================================================================
 *  1. CONSTANTS AND SMALL MATH HELPERS
 * ========================================================================== */

static const float W        = 1000.0f;   // logical screen width  (2D units)
static const float H        = 700.0f;    // logical screen height
static const float HORIZON  = 592.0f;    // 2D view: sea below, sky band above
static const float PI       = 3.14159265358979f;
static const float S3       = 0.06f;     // logical units -> 3D world units

static const float BOAT_R   = 15.0f;
static const float BOAT_MIN_X = 40.0f,  BOAT_MAX_X = 960.0f;
static const float BOAT_MIN_Y = 60.0f,  BOAT_MAX_Y = 380.0f;
static const float SPAWN_Y  = 780.0f;    // objects appear beyond the horizon
static const float COMBO_WINDOW = 2.6f;  // seconds to keep a combo alive
static const float BLOOM_TIME   = 6.0f;  // starburst duration
static const float DYING_TIME   = 1.6f;

struct Color { float r, g, b; };
static inline Color rgb(float r, float g, float b) { Color c = { r, g, b }; return c; }
static inline Color operator+(Color a, Color b) { return rgb(a.r + b.r, a.g + b.g, a.b + b.b); }
static inline Color operator*(Color a, float s) { return rgb(a.r * s, a.g * s, a.b * s); }

static inline float clampf(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
static inline float lerpf(float a, float b, float t)    { return a + (b - a) * t; }
static inline float mxf(float a, float b) { return a > b ? a : b; }
static inline float mnf(float a, float b) { return a < b ? a : b; }
static inline float smooth01(float t)     { t = clampf(t, 0, 1); return t * t * (3 - 2 * t); }
static inline float frand(float lo, float hi) { return lo + (hi - lo) * (float)rand() / (float)RAND_MAX; }
static inline float wrapf(float v, float m) { v = fmodf(v, m); return v < 0 ? v + m : v; }

// [INTERP] linear interpolation between two colours
static inline Color lerpC(Color a, Color b, float t) {
    return rgb(lerpf(a.r, b.r, t), lerpf(a.g, b.g, t), lerpf(a.b, b.b, t));
}
static inline void glColorC(Color c, float a) { glColor4f(c.r, c.g, c.b, a); }

// deterministic pseudo-random value in [0,1] from an integer (for stable layouts)
static inline float hash01(unsigned n) {
    n = (n << 13) ^ n;
    n = n * (n * n * 15731u + 789221u) + 1376312589u;
    return (float)(n & 0x7fffffffu) / 2147483647.0f;
}

struct Vec2 { float x, y; };
struct IPt  { int x, y; };
struct Vec3 { float x, y, z; };
static inline Vec3 v3(float x, float y, float z) { Vec3 v = { x, y, z }; return v; }
static inline Vec3 sub3(Vec3 a, Vec3 b)   { return v3(a.x - b.x, a.y - b.y, a.z - b.z); }
static inline float dot3(Vec3 a, Vec3 b)  { return a.x * b.x + a.y * b.y + a.z * b.z; }
static inline Vec3 cross3(Vec3 a, Vec3 b) {
    return v3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}
static inline Vec3 norm3(Vec3 a) {
    float l = sqrtf(dot3(a, a));
    return l > 1e-6f ? v3(a.x / l, a.y / l, a.z / l) : v3(0, 1, 0);
}

// Named colours
static const Color ORB_COL   = { 0.35f, 1.00f, 0.85f };
static const Color JELLY_COL = { 1.00f, 0.38f, 0.82f };
static const Color GOLD      = { 1.00f, 0.80f, 0.30f };
static const Color BOAT_GLOW = { 0.60f, 0.88f, 1.00f };
static const Color WHITE     = { 1.00f, 1.00f, 1.00f };
static const Color DANGER    = { 1.00f, 0.25f, 0.30f };

/* ============================================================================
 *  2. HAND-WRITTEN RASTER ALGORITHMS
 * ========================================================================== */

// [MIDPOINT] Midpoint circle algorithm.
// Integer-only rasterisation of a circle outline. We walk one octant (x from
// 0 up to y) and use the decision parameter p to choose between the pixel to
// the east (p < 0) or south-east (p >= 0); the other 7 octants come for free
// from the circle's 8-way symmetry. Output: integer offsets from the centre.
static void midpointCircle(int r, std::vector<IPt>& out) {
    out.clear();
    if (r <= 0) { IPt c = { 0, 0 }; out.push_back(c); return; }
    int x = 0, y = r;
    int p = 1 - r;
    while (x <= y) {
        IPt pts[8] = { { x, y }, { -x, y }, { x, -y }, { -x, -y },
                       { y, x }, { -y, x }, { y, -x }, { -y, -x } };
        for (int i = 0; i < 8; ++i) out.push_back(pts[i]);
        ++x;
        if (p < 0) {
            p += 2 * x + 1;            // east pixel
        } else {
            --y;
            p += 2 * (x - y) + 1;      // south-east pixel
        }
    }
}

// [DDA] Digital Differential Analyzer line algorithm.
// Step once per unit along the longer axis and add a constant fractional
// increment (dx/steps, dy/steps) to each coordinate, rounding to the grid.
// Points are appended so several segments can share one buffer.
static void ddaLine(float x0, float y0, float x1, float y1, std::vector<Vec2>& out) {
    float dx = x1 - x0, dy = y1 - y0;
    int steps = (int)mxf(fabsf(dx), fabsf(dy));
    if (steps < 1) { Vec2 p = { floorf(x0 + 0.5f), floorf(y0 + 0.5f) }; out.push_back(p); return; }
    float xInc = dx / steps, yInc = dy / steps;
    float x = x0, y = y0;
    for (int i = 0; i <= steps; ++i) {
        Vec2 p = { floorf(x + 0.5f), floorf(y + 0.5f) };
        out.push_back(p);
        x += xInc;
        y += yInc;
    }
}

/* ============================================================================
 *  3. GAME DATA
 * ========================================================================== */

enum State { ST_START, ST_PLAYING, ST_PAUSED, ST_DYING, ST_GAMEOVER };
enum ObsType { OBS_ROCK, OBS_JELLY };

struct Boat {
    float x, y, vx, vy;
    float light;          // 0..1  - when it hits 0 the game ends
    float tilt;           // degrees, visual banking
    float dashTimer, dashCooldown, invuln, glowPulse;
};

struct Orb { float x, y, phase; bool alive; bool decor; };

struct Obstacle {
    int   type;
    float x, y, r, phase;
    float baseX, swayAmp;
    float shape[10];      // radius jitter for the jagged rock outline
    bool  close, nearDone, hit, alive;
};

struct Particle {
    float x, y, h;        // h = height above the water (used in the 3D view)
    float vx, vy, vh;
    float life, maxLife, size;
    Color c;
};

struct Ripple   { float x, y, r, maxR, life, maxLife; Color c; };
struct TrailPt  { float x, y, life; };
struct Popup    { float x, y, life; std::string text; Color c; };
struct BgStar   { float x, y, phase, bright; };
struct DomeStar { float x, y, z, phase, bright; };

struct ShootingStar {
    bool active;
    float x, y, vx, vy;
    std::deque<Vec2> tail;
};

// Colour palette for one "sky stage". The game blends between stages as the
// score rises, so the whole scene slowly changes mood.
struct Palette { Color skyTop, horizon, seaFar, seaNear, glow; };

static const Palette STAGES[5] = {
    // Midnight blue
    { { 0.005f, 0.01f, 0.04f }, { 0.05f, 0.10f, 0.22f }, { 0.02f, 0.06f, 0.14f }, { 0.00f, 0.015f, 0.05f }, { 0.15f, 0.75f, 1.00f } },
    // Violet
    { { 0.03f, 0.005f, 0.07f }, { 0.22f, 0.08f, 0.32f }, { 0.08f, 0.03f, 0.16f }, { 0.01f, 0.00f, 0.05f }, { 0.45f, 0.55f, 1.00f } },
    // Aurora teal
    { { 0.00f, 0.03f, 0.05f }, { 0.03f, 0.26f, 0.26f }, { 0.01f, 0.10f, 0.12f }, { 0.00f, 0.02f, 0.04f }, { 0.15f, 1.00f, 0.75f } },
    // Rose
    { { 0.05f, 0.00f, 0.05f }, { 0.36f, 0.06f, 0.24f }, { 0.12f, 0.02f, 0.10f }, { 0.02f, 0.00f, 0.03f }, { 1.00f, 0.40f, 0.85f } },
    // Ember dawn
    { { 0.03f, 0.01f, 0.06f }, { 0.42f, 0.20f, 0.08f }, { 0.14f, 0.07f, 0.06f }, { 0.02f, 0.01f, 0.03f }, { 1.00f, 0.78f, 0.35f } },
};
static const float STAGE_SCORE[5] = { 0.0f, 2500.0f, 6000.0f, 11000.0f, 18000.0f };

/* ---- global game state ---------------------------------------------------- */
static State state  = ST_START;
static bool  mode3D = false;

static Boat boat;
static std::vector<Orb>      orbs;
static std::vector<Obstacle> obstacles;
static std::vector<Particle> particles;
static std::vector<Ripple>   ripples;
static std::deque<TrailPt>   trail;
static std::vector<Popup>    popups;
static ShootingStar          star;

static float score = 0, highScore = 0;
static bool  newBest = false;
static int   combo = 0, maxCombo = 0, orbsCollected = 0, nearMisses = 0;
static float comboTimer = 0, comboPulse = 0;
static float elapsed = 0, animTime = 0;
static float scrollSpeed = 70, travel = 0;
static float bloomTimer = 0;
static float shakeTimer = 0, shakeMag = 0;
static float flashAlpha = 0; static Color flashColor = { 1, 1, 1 };
static float orbTimer = 0, obsTimer = 0, starTimer = 0, rippleTimer = 0;
static float dyingTimer = 0, overTimer = 0;
static float bannerTimer = 0; static std::string bannerText;

static bool keyL = false, keyR = false, keyU = false, keyD = false;
static bool dashRequest = false;
static int  lastTicks = 0;

static Palette pal = STAGES[0];
static GLuint  glowTex = 0;

// window / letterboxed viewport
static int   winW = 1000, winH = 700, vpX = 0, vpY = 0, vpW = 1000, vpH = 700;
static float vpScale = 1.0f;

// 3D camera state
static float camX = 0, camZ = 8;
static GLdouble mvSaved[16], projSaved[16];
static GLint    viewSaved[4];
static float camRight[3] = { 1, 0, 0 }, camUp[3] = { 0, 1, 0 };
static float boatScreenX = 500, boatScreenY = 200, lightScreenR = 200;

static std::vector<BgStar>   seaStars, skyStars;
static std::vector<DomeStar> domeStars;
static std::vector<IPt>      circBuf;   // scratch buffers for the algorithms
static std::vector<Vec2>     lineBuf;

static const char* HS_FILE = "starlit_tides_highscore.txt";

/* ============================================================================
 *  4. SMALL GAME HELPERS
 * ========================================================================== */

static float bloomFactor() {             // 0..1, fades in/out smoothly
    if (bloomTimer <= 0) return 0;
    return smooth01(mnf(bloomTimer, BLOOM_TIME - bloomTimer + 0.25f) / 0.6f);
}

static float lightRadius() {             // radius of the boat's light pool
    return 70.0f + boat.light * 190.0f + bloomFactor() * 140.0f + boat.glowPulse * 25.0f;
}

static int multiplier() {
    int m = 1 + combo / 4;
    if (m > 10) m = 10;
    return bloomTimer > 0 ? m * 2 : m;
}

// [INTERP] combo colour: cyan -> magenta -> gold as the combo grows
static Color comboColor() {
    float t = clampf(combo / 20.0f, 0, 1);
    Color cyan = rgb(0.3f, 0.9f, 1.0f), mag = rgb(1.0f, 0.35f, 0.9f);
    return t < 0.5f ? lerpC(cyan, mag, t * 2) : lerpC(mag, GOLD, (t - 0.5f) * 2);
}

static void updatePalette() {
    int i = 0;
    while (i < 4 && score >= STAGE_SCORE[i + 1]) ++i;
    if (i >= 4) { pal = STAGES[4]; return; }
    float t = smooth01((score - STAGE_SCORE[i]) / (STAGE_SCORE[i + 1] - STAGE_SCORE[i]));
    // only blend during the last 35% of each stage so stages feel distinct
    t = smooth01((t - 0.65f) / 0.35f);
    const Palette& a = STAGES[i];
    const Palette& b = STAGES[i + 1];
    pal.skyTop  = lerpC(a.skyTop,  b.skyTop,  t);
    pal.horizon = lerpC(a.horizon, b.horizon, t);
    pal.seaFar  = lerpC(a.seaFar,  b.seaFar,  t);
    pal.seaNear = lerpC(a.seaNear, b.seaNear, t);
    pal.glow    = lerpC(a.glow,    b.glow,    t);
}

static float auroraAmount() { return smooth01((score - 1500.0f) / 4000.0f); }

static void emit(float x, float y, int n, Color c, float spMin, float spMax,
                 float lifeMin, float lifeMax, float size, float up) {
    for (int i = 0; i < n; ++i) {
        Particle p;
        float a  = frand(0, 2 * PI);
        float sp = frand(spMin, spMax);
        p.x = x; p.y = y; p.h = 0.25f;
        p.vx = cosf(a) * sp; p.vy = sinf(a) * sp;
        p.vh = frand(0.4f, 1.0f) * up;
        p.life = p.maxLife = frand(lifeMin, lifeMax);
        p.size = size * frand(0.7f, 1.3f);
        p.c = c;
        particles.push_back(p);
    }
}

static void addRipple(float x, float y, float maxR, float life, Color c) {
    Ripple r = { x, y, 0, maxR, life, life, c };
    ripples.push_back(r);
}

static void addPopup(float x, float y, const std::string& text, Color c) {
    Popup p; p.x = x; p.y = y; p.life = 1.2f; p.text = text; p.c = c;
    popups.push_back(p);
}

static void showBanner(const std::string& s) { bannerText = s; bannerTimer = 1.8f; }

static void loadHighScore() {
    FILE* f = fopen(HS_FILE, "r");
    if (!f) return;
    if (fscanf(f, "%f", &highScore) != 1) highScore = 0;
    fclose(f);
}

static void saveHighScore() {
    FILE* f = fopen(HS_FILE, "w");
    if (!f) return;
    fprintf(f, "%d\n", (int)highScore);
    fclose(f);
}

/* ============================================================================
 *  5. SPAWNING
 * ========================================================================== */

static void spawnOrb(float x, float y, bool decor) {
    // nudge orbs out of rocks so they are always collectable
    for (size_t i = 0; i < obstacles.size(); ++i) {
        const Obstacle& o = obstacles[i];
        float dx = x - o.x, dy = y - o.y;
        if (dx * dx + dy * dy < (o.r + 30) * (o.r + 30))
            x += (x < o.x ? -1.0f : 1.0f) * (o.r + 45);
    }
    Orb o; o.x = clampf(x, 50, W - 50); o.y = y; o.phase = frand(0, 2 * PI);
    o.alive = true; o.decor = decor;
    orbs.push_back(o);
}

static void spawnOrbPattern() {
    int pattern = rand() % 4;
    float x = frand(90, W - 90);
    if (pattern <= 1) {
        spawnOrb(x, SPAWN_Y, false);                              // single
    } else if (pattern == 2) {
        for (int i = 0; i < 5; ++i) spawnOrb(x, SPAWN_Y + i * 42, false);   // straight lane
    } else {
        float amp = frand(60, 120);                               // sine snake
        for (int i = 0; i < 7; ++i) spawnOrb(x + sinf(i * 0.9f) * amp, SPAWN_Y + i * 40, false);
    }
}

static void spawnObstacle(float difficulty) {
    Obstacle o;
    float jellyChance = mnf(0.15f + difficulty * 0.30f, 0.5f);
    o.type  = frand(0, 1) < jellyChance ? OBS_JELLY : OBS_ROCK;
    o.phase = frand(0, 2 * PI);
    if (o.type == OBS_JELLY) {
        o.r = frand(18, 24);
        o.baseX = frand(90, W - 90);
        o.swayAmp = frand(30, 90);
        o.x = o.baseX;
    } else {
        o.r = frand(18, 40);
        o.x = o.baseX = frand(50, W - 50);
        o.swayAmp = 0;
    }
    o.y = SPAWN_Y + frand(0, 60);
    for (int i = 0; i < 10; ++i) o.shape[i] = frand(0.75f, 1.15f);
    o.close = o.nearDone = o.hit = false;
    o.alive = true;
    obstacles.push_back(o);
}

static void spawnStar() {
    bool fromLeft = rand() % 2 == 0;
    star.active = true;
    star.x = fromLeft ? -30.0f : W + 30.0f;
    star.y = frand(630, 690);
    float tx = frand(250, 750), ty = frand(140, 300);   // aims through the boat lane
    float dx = tx - star.x, dy = ty - star.y;
    float len = sqrtf(dx * dx + dy * dy);
    star.vx = dx / len * 360.0f;
    star.vy = dy / len * 360.0f;
    star.tail.clear();
}

/* ============================================================================
 *  6. GAME FLOW
 * ========================================================================== */

static void resetBoat() {
    boat.x = W * 0.5f; boat.y = 130; boat.vx = boat.vy = 0;
    boat.light = 1.0f; boat.tilt = 0;
    boat.dashTimer = 0; boat.dashCooldown = 0; boat.invuln = 0; boat.glowPulse = 0;
}

static void resetGame() {
    resetBoat();
    orbs.clear(); obstacles.clear(); particles.clear(); ripples.clear();
    trail.clear(); popups.clear();
    star.active = false; star.tail.clear();
    score = 0; newBest = false;
    combo = maxCombo = orbsCollected = nearMisses = 0;
    comboTimer = comboPulse = 0;
    elapsed = 0; scrollSpeed = 170; bloomTimer = 0;
    shakeTimer = 0; flashAlpha = 0;
    orbTimer = 0.3f; obsTimer = 1.6f; starTimer = frand(10, 16); rippleTimer = 0;
    dashRequest = false;
    state = ST_PLAYING;
    addRipple(boat.x, boat.y, 160, 1.2f, BOAT_GLOW);
    emit(boat.x, boat.y, 40, BOAT_GLOW, 60, 220, 0.5f, 1.1f, 9, 2.5f);
}

static void startDying() {
    state = ST_DYING;
    dyingTimer = DYING_TIME;
    boat.light = 0;
    shakeTimer = 0.4f; shakeMag = 10;
    if (score > highScore) { highScore = score; newBest = true; saveHighScore(); }
}

static void collectOrb(Orb& o) {
    o.alive = false;
    ++combo; ++orbsCollected;
    if (combo > maxCombo) maxCombo = combo;
    comboTimer = COMBO_WINDOW;
    comboPulse = 1.0f;
    int m = multiplier();
    int pts = 100 * m;
    score += pts;
    boat.light = mnf(1.0f, boat.light + 0.12f);
    boat.glowPulse = 1.0f;

    emit(o.x, o.y, 18, ORB_COL, 40, 200, 0.4f, 0.9f, 8, 3.0f);   // [PARTICLES] sparkles
    emit(o.x, o.y, 6, WHITE, 20, 90, 0.3f, 0.6f, 5, 2.0f);
    addRipple(o.x, o.y, 55, 0.8f, ORB_COL);

    char buf[48];
    if (m > 1) snprintf(buf, sizeof buf, "+%d x%d", pts, m);
    else       snprintf(buf, sizeof buf, "+%d", pts);
    addPopup(o.x, o.y + 22, buf, ORB_COL);

    if (combo % 10 == 0) {
        snprintf(buf, sizeof buf, "COMBO %d!", combo);
        addPopup(boat.x, boat.y + 60, buf, comboColor());
        addRipple(boat.x, boat.y, 150, 1.2f, comboColor());
    }
}

static void hitObstacle(Obstacle& ob, float dx, float dy, float dist) {
    ob.hit = true;
    boat.light -= 0.26f;
    combo = 0; comboTimer = 0;
    shakeTimer = 0.45f; shakeMag = 16;                       // screen shake
    flashAlpha = 0.55f;                                       // screen flash
    flashColor = ob.type == OBS_JELLY ? JELLY_COL : DANGER;
    boat.invuln = 1.3f;
    if (dist < 0.001f) { dx = 0; dy = 1; dist = 1; }
    boat.vx -= dx / dist * 520.0f;                            // knock-back
    boat.vy -= dy / dist * 520.0f;
    Color c = ob.type == OBS_JELLY ? JELLY_COL : rgb(1.0f, 0.55f, 0.45f);
    emit(boat.x, boat.y, 30, c, 60, 260, 0.4f, 1.0f, 9, 3.5f);
    emit(boat.x, boat.y, 10, WHITE, 40, 160, 0.3f, 0.6f, 6, 2.0f);
    addRipple(boat.x, boat.y, 90, 0.7f, c);
    addPopup(boat.x, boat.y + 40, ob.type == OBS_JELLY ? "STUNG!" : "CRASH!", c);
}

static void nearMiss(Obstacle& ob) {
    ob.nearDone = true;
    ++combo; ++nearMisses;
    if (combo > maxCombo) maxCombo = combo;
    comboTimer = COMBO_WINDOW;
    comboPulse = 1.0f;
    int pts = 50 * multiplier();
    score += pts;
    boat.glowPulse = mxf(boat.glowPulse, 0.6f);
    char buf[48];
    snprintf(buf, sizeof buf, "NEAR MISS +%d", pts);
    addPopup(boat.x, boat.y + 44, buf, rgb(0.85f, 0.95f, 1.0f));
    emit(boat.x, boat.y, 10, comboColor(), 40, 150, 0.3f, 0.7f, 6, 2.0f);
}

static void catchStar() {
    star.active = false;
    bloomTimer = BLOOM_TIME;
    boat.light = 1.0f;
    boat.glowPulse = 1.0f;
    int pts = 500 * multiplier();
    score += pts;
    flashAlpha = 0.7f; flashColor = GOLD;
    shakeTimer = 0.25f; shakeMag = 6;
    emit(boat.x, boat.y, 70, GOLD, 80, 380, 0.6f, 1.4f, 10, 4.0f);
    emit(boat.x, boat.y, 30, WHITE, 40, 200, 0.4f, 0.9f, 7, 3.0f);
    addRipple(boat.x, boat.y, 140, 0.9f, GOLD);
    addRipple(boat.x, boat.y, 240, 1.3f, GOLD);
    addRipple(boat.x, boat.y, 360, 1.8f, WHITE);
    char buf[48];
    snprintf(buf, sizeof buf, "STARBURST! +%d", pts);
    addPopup(boat.x, boat.y + 60, buf, GOLD);
}

/* ============================================================================
 *  7. UPDATE
 * ========================================================================== */

static void pushTrail() {
    TrailPt t = { boat.x, boat.y - 10, 1.0f };
    trail.push_front(t);
    while (trail.size() > 70) trail.pop_back();
}

static void emitWake(float dt) {
    if (frand(0, 1) < dt * 30.0f) {
        Particle p;
        p.x = boat.x + frand(-7, 7); p.y = boat.y - 20; p.h = 0.05f;
        p.vx = frand(-25, 25); p.vy = frand(-30, -5); p.vh = frand(0.1f, 0.5f);
        p.life = p.maxLife = frand(0.5f, 1.0f);
        p.size = frand(4, 8);
        p.c = lerpC(pal.glow, BOAT_GLOW, 0.5f);
        particles.push_back(p);
    }
    rippleTimer -= dt;
    if (rippleTimer <= 0) {
        rippleTimer = 0.45f;
        addRipple(boat.x, boat.y - 6, 38, 1.0f, pal.glow);
    }
}

// Moves everything that floats on the water toward the player.
static void updateWorldScroll(float dt) {
    travel += scrollSpeed * dt;
    for (size_t i = 0; i < orbs.size(); ++i) orbs[i].y -= scrollSpeed * dt;
    for (size_t i = 0; i < obstacles.size(); ++i) {
        Obstacle& o = obstacles[i];
        if (o.type == OBS_JELLY) {
            o.y -= scrollSpeed * 0.88f * dt;   // jellies swim slowly upstream
            o.x = o.baseX + sinf(animTime * 1.4f + o.phase) * o.swayAmp;
        } else {
            o.y -= scrollSpeed * dt;
        }
        if (o.y < -90) o.alive = false;
    }
    for (size_t i = 0; i < orbs.size(); ++i) if (orbs[i].y < -60) orbs[i].alive = false;

    // compact the arrays (erase dead objects)
    size_t n = 0;
    for (size_t i = 0; i < orbs.size(); ++i) if (orbs[i].alive) orbs[n++] = orbs[i];
    orbs.resize(n);
    n = 0;
    for (size_t i = 0; i < obstacles.size(); ++i) if (obstacles[i].alive) obstacles[n++] = obstacles[i];
    obstacles.resize(n);
}

static void updateAmbient(float dt) {          // start screen: idle boat, decor orbs
    updateWorldScroll(dt);
    float px = boat.x;
    boat.x = W * 0.5f + sinf(animTime * 0.5f) * 80.0f;
    boat.y = 115;
    boat.vx = (boat.x - px) / mxf(dt, 1e-4f);
    boat.tilt = -boat.vx * 0.12f;
    boat.light = 1.0f;
    orbTimer -= dt;
    if (orbTimer <= 0) { orbTimer = 0.9f; spawnOrb(frand(80, W - 80), SPAWN_Y, true); }
    pushTrail();
    emitWake(dt);
}

static void updatePlaying(float dt) {
    elapsed += dt;
    float difficulty = elapsed / 60.0f;                  // grows by 1 per minute
    scrollSpeed = mnf(170.0f + elapsed * 5.0f, 560.0f);  // rising excitement

    /* ---- movement ---- */
    float ix = (keyR ? 1.0f : 0.0f) - (keyL ? 1.0f : 0.0f);
    float iy = (keyU ? 1.0f : 0.0f) - (keyD ? 1.0f : 0.0f);
    float il = sqrtf(ix * ix + iy * iy);
    if (il > 0) { ix /= il; iy /= il; }
    const float accel = 2600.0f;
    boat.vx += ix * accel * dt;
    boat.vy += iy * accel * dt;
    float damp = expf(-6.0f * dt);
    boat.vx *= damp; boat.vy *= damp;

    boat.dashTimer   -= dt;
    boat.dashCooldown -= dt;
    boat.invuln      -= dt;

    if (dashRequest) {
        dashRequest = false;
        if (boat.dashCooldown <= 0) {
            float dx = ix, dy = iy;
            if (il == 0) { dx = 0; dy = 1; }
            boat.vx = dx * 950.0f; boat.vy = dy * 950.0f;
            boat.dashTimer = 0.2f;
            boat.dashCooldown = 1.1f;
            boat.invuln = mxf(boat.invuln, 0.3f);
            emit(boat.x, boat.y, 22, BOAT_GLOW, 50, 220, 0.3f, 0.7f, 7, 2.0f);
            addRipple(boat.x, boat.y, 70, 0.5f, WHITE);
        }
    }
    if (boat.dashTimer <= 0) {
        float sp = sqrtf(boat.vx * boat.vx + boat.vy * boat.vy);
        if (sp > 420.0f) { boat.vx *= 420.0f / sp; boat.vy *= 420.0f / sp; }
    }
    boat.x += boat.vx * dt;
    boat.y += boat.vy * dt;
    if (boat.x < BOAT_MIN_X) { boat.x = BOAT_MIN_X; boat.vx = 0; }
    if (boat.x > BOAT_MAX_X) { boat.x = BOAT_MAX_X; boat.vx = 0; }
    if (boat.y < BOAT_MIN_Y) { boat.y = BOAT_MIN_Y; boat.vy = 0; }
    if (boat.y > BOAT_MAX_Y) { boat.y = BOAT_MAX_Y; boat.vy = 0; }
    boat.tilt = lerpf(boat.tilt, -boat.vx * 0.06f, mnf(1.0f, 10.0f * dt));

    /* ---- fading light ---- */
    if (bloomTimer > 0) {
        bloomTimer -= dt;
        boat.light = mnf(1.0f, boat.light + 0.3f * dt);
    } else {
        boat.light -= (0.055f + 0.03f * mnf(difficulty, 2.0f)) * dt;
    }

    updateWorldScroll(dt);

    /* ---- starburst magnet: orbs drift toward the boat ---- */
    if (bloomTimer > 0) {
        for (size_t i = 0; i < orbs.size(); ++i) {
            Orb& o = orbs[i];
            float dx = boat.x - o.x, dy = boat.y - o.y;
            float d = sqrtf(dx * dx + dy * dy);
            if (d < 280 && d > 1) { o.x += dx / d * 520 * dt; o.y += dy / d * 520 * dt; }
        }
    }

    /* ---- spawning (denser over time) ---- */
    orbTimer -= dt * (bloomTimer > 0 ? 2.0f : 1.0f);
    if (orbTimer <= 0) { spawnOrbPattern(); orbTimer = frand(0.7f, 1.25f); }
    obsTimer -= dt;
    if (obsTimer <= 0) {
        spawnObstacle(difficulty);
        obsTimer = mxf(0.38f, 1.35f - difficulty * 0.55f) * frand(0.7f, 1.3f);
    }

    /* ---- [COLLISION] orbs ---- */
    for (size_t i = 0; i < orbs.size(); ++i) {
        Orb& o = orbs[i];
        if (!o.alive || o.decor) continue;
        float dx = o.x - boat.x, dy = o.y - boat.y;
        float rr = BOAT_R + 16.0f;
        if (dx * dx + dy * dy < rr * rr) collectOrb(o);
    }

    /* ---- [COLLISION] obstacles + near-miss detection ---- */
    for (size_t i = 0; i < obstacles.size(); ++i) {
        Obstacle& ob = obstacles[i];
        float dx = ob.x - boat.x, dy = ob.y - boat.y;
        float dist = sqrtf(dx * dx + dy * dy);
        float hitR = ob.r * 0.82f + BOAT_R * 0.8f;
        if (!ob.hit && dist < hitR) {
            if (boat.dashTimer > 0)      ob.close = true;   // dashing through = daring
            else if (boat.invuln <= 0)   hitObstacle(ob, dx, dy, dist);
        } else if (dist < hitR + 34.0f && boat.invuln <= 0) {
            ob.close = true;
        }
        // obstacle has fully passed the boat without hitting it -> bonus
        if (ob.close && !ob.hit && !ob.nearDone && ob.y + ob.r < boat.y - BOAT_R)
            nearMiss(ob);
    }

    /* ---- combo window ---- */
    if (combo > 0) {
        comboTimer -= dt;
        if (comboTimer <= 0) combo = 0;
    }

    /* ---- golden shooting star ---- */
    starTimer -= dt;
    if (!star.active && starTimer <= 0) { spawnStar(); starTimer = frand(13, 22); }
    if (star.active) {
        star.x += star.vx * dt;
        star.y += star.vy * dt;
        Vec2 p = { star.x, star.y };
        star.tail.push_front(p);
        while (star.tail.size() > 26) star.tail.pop_back();
        if (frand(0, 1) < 0.7f) {
            Particle q;
            q.x = star.x; q.y = star.y; q.h = 1.0f;
            q.vx = frand(-30, 30); q.vy = frand(-30, 30); q.vh = 0;
            q.life = q.maxLife = frand(0.4f, 0.8f); q.size = frand(4, 7); q.c = GOLD;
            particles.push_back(q);
        }
        float dx = star.x - boat.x, dy = star.y - boat.y;
        float rr = BOAT_R + 26.0f;
        if (dx * dx + dy * dy < rr * rr) catchStar();
        else if (star.x < -100 || star.x > W + 100 || star.y < -100) star.active = false;
    }

    /* ---- score ---- */
    score += scrollSpeed * dt * 0.04f * multiplier();

    pushTrail();
    emitWake(dt);

    if (boat.light <= 0) startDying();
}

// Effects that run in every state (particles, ripples, camera, ...)
static void updateEffects(float dt) {
    // [PARTICLES]
    float damp = expf(-2.2f * dt);
    size_t n = 0;
    for (size_t i = 0; i < particles.size(); ++i) {
        Particle& p = particles[i];
        p.life -= dt;
        if (p.life <= 0) continue;
        p.x += p.vx * dt;
        p.y += p.vy * dt - scrollSpeed * dt;   // particles drift with the water
        p.vx *= damp; p.vy *= damp;
        p.h += p.vh * dt;
        p.vh -= 6.0f * dt;                     // gravity (3D view only)
        if (p.h < 0) { p.h = 0; p.vh = 0; }
        particles[n++] = p;
    }
    particles.resize(n);

    n = 0;
    for (size_t i = 0; i < ripples.size(); ++i) {
        Ripple& r = ripples[i];
        r.life -= dt;
        if (r.life <= 0) continue;
        float t = 1.0f - r.life / r.maxLife;
        r.r = r.maxR * (1.0f - (1.0f - t) * (1.0f - t));   // ease-out growth
        r.y -= scrollSpeed * dt;
        ripples[n++] = r;
    }
    ripples.resize(n);

    n = 0;
    for (size_t i = 0; i < popups.size(); ++i) {
        Popup& p = popups[i];
        p.life -= dt;
        if (p.life <= 0) continue;
        p.y += 40.0f * dt;
        popups[n++] = p;
    }
    popups.resize(n);

    for (size_t i = 0; i < trail.size(); ++i) {
        trail[i].y -= scrollSpeed * dt;
        trail[i].life -= dt * 1.6f;
    }
    while (!trail.empty() && trail.back().life <= 0) trail.pop_back();

    shakeTimer  = mxf(0.0f, shakeTimer - dt);
    flashAlpha  = mxf(0.0f, flashAlpha - dt * 1.6f);
    comboPulse  = mxf(0.0f, comboPulse - dt * 3.0f);
    bannerTimer = mxf(0.0f, bannerTimer - dt);
    boat.glowPulse = mxf(0.0f, boat.glowPulse - dt * 1.5f);

    // smooth chase camera for the 3D view
    float tx = (boat.x - W * 0.5f) * S3 * 0.75f;
    float tz = -boat.y * S3 + 10.5f;
    camX += (tx - camX) * mnf(1.0f, 4.0f * dt);
    camZ += (tz - camZ) * mnf(1.0f, 3.0f * dt);

    updatePalette();
}

static void update(float dt) {
    if (state == ST_PAUSED) return;          // freeze everything
    animTime += dt;
    switch (state) {
    case ST_START:
        scrollSpeed = 70;
        updateAmbient(dt);
        break;
    case ST_PLAYING:
        updatePlaying(dt);
        break;
    case ST_DYING:
        scrollSpeed *= expf(-1.5f * dt);
        updateWorldScroll(dt);
        dyingTimer -= dt;
        if (dyingTimer <= 0) { state = ST_GAMEOVER; overTimer = 0; }
        break;
    case ST_GAMEOVER:
        scrollSpeed = lerpf(scrollSpeed, 50.0f, mnf(1.0f, dt));
        overTimer += dt;
        updateWorldScroll(dt);
        break;
    default: break;
    }
    updateEffects(dt);
}

/* ============================================================================
 *  8. RENDER HELPERS
 * ========================================================================== */

// [BLEND] additive blending makes overlapping glows add up into bright light
static void blendAdd()   { glBlendFunc(GL_SRC_ALPHA, GL_ONE); }
static void blendAlpha() { glBlendFunc(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA); }

// Builds a soft radial-falloff texture once; every glow sprite uses it.
static void makeGlowTexture() {
    const int N = 64;
    static unsigned char data[N * N * 4];
    for (int y = 0; y < N; ++y) {
        for (int x = 0; x < N; ++x) {
            float dx = (x + 0.5f) / N * 2 - 1, dy = (y + 0.5f) / N * 2 - 1;
            float d = sqrtf(dx * dx + dy * dy);
            float a = d >= 1 ? 0 : expf(-d * d * 4.0f) * (1 - d);
            unsigned char* p = &data[(y * N + x) * 4];
            p[0] = p[1] = p[2] = 255;
            p[3] = (unsigned char)(clampf(a * 1.15f, 0, 1) * 255);
        }
    }
    glGenTextures(1, &glowTex);
    glBindTexture(GL_TEXTURE_2D, glowTex);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP);
    glTexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, N, N, 0, GL_RGBA, GL_UNSIGNED_BYTE, data);
}

static void glowBegin() {
    glEnable(GL_TEXTURE_2D);
    glBindTexture(GL_TEXTURE_2D, glowTex);
    blendAdd();
    glBegin(GL_QUADS);
}
static void glowEnd() { glEnd(); glDisable(GL_TEXTURE_2D); }

static void glow2D(float x, float y, float r, Color c, float a) {
    glColorC(c, a);
    glTexCoord2f(0, 0); glVertex2f(x - r, y - r);
    glTexCoord2f(1, 0); glVertex2f(x + r, y - r);
    glTexCoord2f(1, 1); glVertex2f(x + r, y + r);
    glTexCoord2f(0, 1); glVertex2f(x - r, y + r);
}

// camera-facing quad (billboard) in 3D using the camera's right/up vectors
static void glow3D(float x, float y, float z, float s, Color c, float a) {
    float rx = camRight[0] * s, ry = camRight[1] * s, rz = camRight[2] * s;
    float ux = camUp[0] * s,    uy = camUp[1] * s,    uz = camUp[2] * s;
    glColorC(c, a);
    glTexCoord2f(0, 0); glVertex3f(x - rx - ux, y - ry - uy, z - rz - uz);
    glTexCoord2f(1, 0); glVertex3f(x + rx - ux, y + ry - uy, z + rz - uz);
    glTexCoord2f(1, 1); glVertex3f(x + rx + ux, y + ry + uy, z + rz + uz);
    glTexCoord2f(0, 1); glVertex3f(x - rx + ux, y - ry + uy, z - rz + uz);
}

// [INTERP] filled disc whose colour/alpha is interpolated centre -> rim
static void gradientDisc(float x, float y, float r, Color ci, float ai, Color co, float ao, int seg) {
    glBegin(GL_TRIANGLE_FAN);
    glColorC(ci, ai);
    glVertex2f(x, y);
    glColorC(co, ao);
    for (int i = 0; i <= seg; ++i) {
        float a = i * 2 * PI / seg;
        glVertex2f(x + cosf(a) * r, y + sinf(a) * r);
    }
    glEnd();
}

// [MIDPOINT] draws a circle outline as points produced by our algorithm.
// `arc` (0..1) keeps only part of the ring, clockwise from 12 o'clock.
static void midpointRing2D(float cx, float cy, float r, Color c, float a, float psize, float arc) {
    midpointCircle((int)(r + 0.5f), circBuf);
    glPointSize(mxf(1.0f, psize * vpScale));
    glColorC(c, a);
    glBegin(GL_POINTS);
    for (size_t i = 0; i < circBuf.size(); ++i) {
        const IPt& p = circBuf[i];
        if (arc < 1.0f) {
            float ang = atan2f((float)p.x, (float)p.y);   // 0 at top, clockwise
            if (ang < 0) ang += 2 * PI;
            if (ang > arc * 2 * PI) continue;
        }
        glVertex2f(cx + p.x, cy + p.y);
    }
    glEnd();
}

// [DDA] draws a line as points produced by our algorithm, alpha fading a0->a1
static void ddaLine2D(float x0, float y0, float x1, float y1, Color c, float a0, float a1, float psize) {
    lineBuf.clear();
    ddaLine(x0, y0, x1, y1, lineBuf);
    glPointSize(mxf(1.0f, psize * vpScale));
    glBegin(GL_POINTS);
    size_t n = lineBuf.size();
    for (size_t i = 0; i < n; ++i) {
        float t = n > 1 ? (float)i / (n - 1) : 0;
        glColorC(c, lerpf(a0, a1, t));
        glVertex2f(lineBuf[i].x, lineBuf[i].y);
    }
    glEnd();
}

static void bitmapText(float x, float y, const char* s, void* font) {
    glRasterPos2f(x, y);
    for (; *s; ++s) glutBitmapCharacter(font, *s);
}
static float bitmapWidth(const char* s, void* font) {   // in logical units
    int w = 0;
    for (; *s; ++s) w += glutBitmapWidth(font, *s);
    return w / vpScale;
}
static void bitmapTextCentered(float cx, float y, const char* s, void* font, Color c, float a) {
    glColorC(c, a);
    bitmapText(cx - bitmapWidth(s, font) * 0.5f, y, s, font);
}

// Vector (stroke) text, scalable, with an optional neon glow
static void strokeText(float cx, float y, float height, const char* s, Color c, float a, bool glow) {
    float sc = height / 119.05f;
    float w = 0;
    for (const char* p = s; *p; ++p) w += glutStrokeWidth(GLUT_STROKE_ROMAN, *p);
    w *= sc;
    int passes = glow ? 3 : 1;
    for (int pass = 0; pass < passes; ++pass) {
        float lw = 2.0f, alpha = a;
        if (glow && pass == 0) { lw = 9.0f; alpha = a * 0.12f; }
        if (glow && pass == 1) { lw = 5.0f; alpha = a * 0.25f; }
        if (glow && pass < 2) blendAdd(); else blendAlpha();
        glLineWidth(lw);
        glColorC(pass == 2 || !glow ? lerpC(c, WHITE, 0.35f) : c, alpha);
        glPushMatrix();
        glTranslatef(cx - w * 0.5f, y, 0);       // [TRANSFORM]
        glScalef(sc, sc, 1);
        for (const char* p = s; *p; ++p) glutStrokeCharacter(GLUT_STROKE_ROMAN, *p);
        glPopMatrix();
    }
    glLineWidth(1.0f);
    blendAlpha();
}

static void shakeOffset(float& sx, float& sy) {
    sx = sy = 0;
    if (shakeTimer > 0) {
        float k = shakeTimer / 0.45f;
        sx = frand(-1, 1) * shakeMag * k;
        sy = frand(-1, 1) * shakeMag * k;
    }
}

// Darkness that closes in around the light. A transparent hole of radius r,
// a soft gradient ring, then solid dark beyond.  [BLEND] [INTERP]
static void drawDarkness(float cx, float cy, float r, float strength) {
    if (strength <= 0.001f) return;
    blendAlpha();
    const int SEG = 56;
    float r2 = r * 2.1f + 40.0f, r3 = 2600.0f;
    Color dark = rgb(0.0f, 0.0f, 0.012f);
    glBegin(GL_TRIANGLE_STRIP);
    for (int i = 0; i <= SEG; ++i) {
        float a = i * 2 * PI / SEG, c = cosf(a), s = sinf(a);
        glColorC(dark, 0.0f);     glVertex2f(cx + c * r,  cy + s * r);
        glColorC(dark, strength); glVertex2f(cx + c * r2, cy + s * r2);
    }
    glEnd();
    glBegin(GL_TRIANGLE_STRIP);
    for (int i = 0; i <= SEG; ++i) {
        float a = i * 2 * PI / SEG, c = cosf(a), s = sinf(a);
        glColorC(dark, strength);
        glVertex2f(cx + c * r2, cy + s * r2);
        glVertex2f(cx + c * r3, cy + s * r3);
    }
    glEnd();
}

static void darknessParams(float& r, float& s) {
    float base = 0.30f + (1.0f - boat.light) * 0.62f;
    base *= (1.0f - bloomFactor() * 0.9f);
    switch (state) {
    case ST_START:    r = 340; s = 0.30f; break;
    case ST_DYING: {
        float k = smooth01(1.0f - dyingTimer / DYING_TIME);
        r = lightRadius() * (1.0f - k);
        s = lerpf(base, 0.97f, k);
        break;
    }
    case ST_GAMEOVER: r = 0;   s = 0.80f; break;
    default:          r = lightRadius(); s = base; break;
    }
}

/* ============================================================================
 *  9. 2D VIEW  (top-down sea with a sky band at the horizon)
 * ========================================================================== */

static void begin2D() {
    // [PROJECTION] orthographic: 1 unit = 1 logical pixel
    glMatrixMode(GL_PROJECTION);
    glLoadIdentity();
    gluOrtho2D(0, W, 0, H);
    glMatrixMode(GL_MODELVIEW);
    glLoadIdentity();
    glDisable(GL_DEPTH_TEST);
    glDisable(GL_FOG);
}

static void drawSea2D() {
    float bf = bloomFactor();
    Color nearC = pal.seaNear + GOLD * (0.10f * bf);
    Color farC  = pal.seaFar  + GOLD * (0.16f * bf);
    blendAlpha();
    glBegin(GL_QUADS);                              // [INTERP] vertical gradient
    glColorC(nearC, 1); glVertex2f(0, 0); glVertex2f(W, 0);
    glColorC(farC, 1);  glVertex2f(W, HORIZON); glVertex2f(0, HORIZON);
    glEnd();

    // reflections of stars shimmering on the water
    blendAdd();
    glPointSize(2.0f * vpScale);
    glBegin(GL_POINTS);
    for (size_t i = 0; i < seaStars.size(); ++i) {
        const BgStar& s = seaStars[i];
        float y  = wrapf(s.y - travel * 0.25f, HORIZON);
        float tw = 0.5f + 0.5f * sinf(animTime * 2.3f + s.phase);
        glColor4f(0.7f, 0.85f, 1.0f, s.bright * tw * 0.35f);
        glVertex2f(s.x + 3 * sinf(animTime + s.phase), y);
    }
    glEnd();
}

// Glowing bioluminescent wave crests. Brightness is interpolated per vertex
// from a shimmer term, the boat's light pool and the starburst.
static void drawWaves2D() {
    const float spacing = 46.0f;
    const int   MAXS = 80;
    float xs[MAXS], ys[MAXS], as[MAXS];
    float R  = lightRadius();
    float bf = bloomFactor();
    bool alive = state == ST_PLAYING || state == ST_PAUSED || state == ST_START;
    float lit = alive ? 0.25f + 0.75f * boat.light : 0.0f;
    int   base = (int)floorf(travel / spacing);
    float off  = travel - base * spacing;
    blendAdd();
    for (int k = 0; k * spacing - off < HORIZON + 12; ++k) {
        float y0 = k * spacing - off;
        float ph = (base + k) * 1.731f;
        int n = 0;
        for (float x = -16; x <= W + 16 && n < MAXS; x += 16, ++n) {
            float y = y0 + 8 * sinf(x * 0.012f + ph + animTime * 1.1f)
                         + 4 * sinf(x * 0.029f - animTime * 1.7f + ph * 2.0f);
            float shimmer = 0.08f + 0.12f * (0.5f + 0.5f * sinf(x * 0.021f + ph * 3.1f + animTime * 1.4f));
            float dx = x - boat.x, dy = y - boat.y;
            float boatLit = lit * expf(-(dx * dx + dy * dy) / (R * R)) * 0.95f;
            xs[n] = x; ys[n] = y;
            as[n] = clampf(shimmer + boatLit + bf * 0.55f, 0, 1.3f);
        }
        glBegin(GL_TRIANGLE_STRIP);                 // soft lower edge -> crest
        for (int i = 0; i < n; ++i) {
            Color c = lerpC(pal.glow, WHITE, clampf(as[i] - 0.8f, 0, 1) * 0.6f);
            glColorC(c, 0);     glVertex2f(xs[i], ys[i] - 10);
            glColorC(c, as[i]); glVertex2f(xs[i], ys[i]);
        }
        glEnd();
        glBegin(GL_TRIANGLE_STRIP);                 // crest -> soft upper edge
        for (int i = 0; i < n; ++i) {
            Color c = lerpC(pal.glow, WHITE, clampf(as[i] - 0.8f, 0, 1) * 0.6f);
            glColorC(c, as[i]); glVertex2f(xs[i], ys[i]);
            glColorC(c, 0);     glVertex2f(xs[i], ys[i] + 3);
        }
        glEnd();
    }

    // the boat's pool of light on the water
    if (alive) gradientDisc(boat.x, boat.y, R * 1.05f, pal.glow, 0.20f * lit + 0.15f * boat.glowPulse,
                            pal.glow, 0.0f, 48);
}

// [DDA] motion streaks that appear as the sea speeds up
static void drawSpeedStreaks2D() {
    float k = clampf((scrollSpeed - 260.0f) / 250.0f, 0, 1);
    if (k <= 0) return;
    blendAdd();
    for (int i = 0; i < 26; ++i) {
        float x   = hash01(i * 7 + 1) * W;
        float len = 40 + 70 * hash01(i * 13 + 5);
        float y   = wrapf(hash01(i * 31 + 9) * 1400.0f - travel * 1.6f, HORIZON + 160.0f) - 80.0f;
        ddaLine2D(x, y, x, y + len, pal.glow, 0.0f, 0.35f * k, 1.4f);
    }
}

static void drawRipples2D() {
    blendAdd();
    for (size_t i = 0; i < ripples.size(); ++i) {
        const Ripple& r = ripples[i];
        if (r.y > HORIZON) continue;
        midpointRing2D(r.x, r.y, r.r, r.c, 0.55f * r.life / r.maxLife, 1.6f, 1.0f);
    }
}

static void drawTrail2D() {
    if (trail.size() < 2) return;
    Color cc = comboColor();
    float boost = 0.5f + mnf((float)combo, 20.0f) * 0.035f + bloomFactor() * 0.5f;
    float wScale = 1.0f + mnf((float)combo, 20.0f) * 0.03f;
    blendAdd();
    for (int side = -1; side <= 1; side += 2) {     // two strips -> soft edges
        glBegin(GL_TRIANGLE_STRIP);
        for (size_t i = 0; i < trail.size(); ++i) {
            const TrailPt& p = trail[i];
            const TrailPt& q = trail[i + 1 < trail.size() ? i + 1 : i - 1];
            float tx = q.x - p.x, ty = q.y - p.y;
            if (i + 1 >= trail.size()) { tx = -tx; ty = -ty; }
            float tl = sqrtf(tx * tx + ty * ty);
            if (tl < 1e-4f) { tx = 0; ty = -1; tl = 1; }
            float nx = -ty / tl, ny = tx / tl;
            float w = (4.0f + 8.0f * p.life) * wScale;
            float a = p.life * 0.45f * boost;
            glColorC(cc, a); glVertex2f(p.x, p.y);
            glColorC(cc, 0); glVertex2f(p.x + nx * w * side, p.y + ny * w * side);
        }
        glEnd();
    }
}

static void drawRock2D(const Obstacle& o) {
    float dx = o.x - boat.x, dy = o.y - boat.y;
    float d  = sqrtf(dx * dx + dy * dy);
    bool alive = state == ST_PLAYING || state == ST_PAUSED;
    float lit = clampf(1.0f - d / (lightRadius() + o.r), 0, 1) * (alive ? boat.light : 0) + bloomFactor() * 0.7f;
    lit = clampf(lit, 0, 1);

    // water lapping around the rock: [MIDPOINT] foam ring
    blendAdd();
    midpointRing2D(o.x, o.y, o.r * 1.05f + 7 + 2 * sinf(animTime * 2 + o.phase), pal.glow,
                   0.18f + 0.35f * lit, 1.5f, 1.0f);

    // jagged polygon with a lit centre and dark rim [INTERP]
    Color ci = lerpC(rgb(0.06f, 0.07f, 0.10f), rgb(0.30f, 0.36f, 0.46f), lit);
    Color co = lerpC(rgb(0.02f, 0.025f, 0.04f), rgb(0.12f, 0.16f, 0.24f), lit);
    blendAlpha();
    glBegin(GL_TRIANGLE_FAN);
    glColorC(ci, 1); glVertex2f(o.x - dx / mxf(d, 1) * o.r * 0.25f, o.y - dy / mxf(d, 1) * o.r * 0.25f);
    glColorC(co, 1);
    for (int i = 0; i <= 10; ++i) {
        float a = i * 2 * PI / 10;
        float rr = o.r * o.shape[i % 10];
        glVertex2f(o.x + cosf(a) * rr, o.y + sinf(a) * rr);
    }
    glEnd();
    blendAdd();
    glLineWidth(1.5f);
    glColorC(pal.glow, 0.10f + 0.6f * lit);
    glBegin(GL_LINE_LOOP);
    for (int i = 0; i < 10; ++i) {
        float a = i * 2 * PI / 10;
        float rr = o.r * o.shape[i];
        glVertex2f(o.x + cosf(a) * rr, o.y + sinf(a) * rr);
    }
    glEnd();
    glLineWidth(1.0f);
}

static void drawJelly2D(const Obstacle& o) {
    float p  = sinf(animTime * 3.2f + o.phase);                // pulsing bell
    float rx = o.r * (1.0f + 0.12f * p);
    float ry = o.r * 0.9f * (1.0f - 0.10f * p);

    glowBegin();
    glow2D(o.x, o.y, o.r * 3.4f, JELLY_COL, 0.30f + 0.1f * p);
    glowEnd();

    // [DDA] tentacles: each one is 4 wiggling segments rasterised by DDA
    blendAdd();
    for (int k = 0; k < 5; ++k) {
        float px = o.x + (k - 2) * rx * 0.33f, py = o.y - 2;
        for (int s = 1; s <= 4; ++s) {
            float nx = o.x + (k - 2) * rx * 0.33f + sinf(animTime * 4 + k * 1.3f + s * 0.9f) * 1.6f * s;
            float ny = o.y - 2 - s * 9.0f;
            ddaLine2D(px, py, nx, ny, JELLY_COL, 0.85f - (s - 1) * 0.2f, 0.85f - s * 0.2f, 1.6f);
            px = nx; py = ny;
        }
    }

    // bell: half-ellipse with whitish centre -> pink rim [INTERP]
    glBegin(GL_TRIANGLE_FAN);
    glColor4f(1.0f, 0.88f, 0.96f, 0.9f);
    glVertex2f(o.x, o.y + ry * 0.25f);
    glColorC(JELLY_COL, 0.55f);
    for (int i = 0; i <= 20; ++i) {
        float a = i * PI / 20;
        glVertex2f(o.x + cosf(a) * rx, o.y + sinf(a) * ry - 2);
    }
    glEnd();
    // wavy skirt
    glLineWidth(1.5f);
    glColorC(JELLY_COL, 0.8f);
    glBegin(GL_LINE_STRIP);
    for (int i = 0; i <= 12; ++i) {
        float x = o.x - rx + i * (2 * rx / 12);
        glVertex2f(x, o.y - 2 + 1.5f * sinf(i * 1.6f + animTime * 6));
    }
    glEnd();
    glLineWidth(1.0f);
}

static void drawOrb2D(const Orb& o) {
    float bob = sinf(animTime * 3 + o.phase);
    glowBegin();
    glow2D(o.x, o.y, 30 + 3 * bob, ORB_COL, 0.45f);
    glow2D(o.x, o.y, 13, ORB_COL, 0.8f);
    glowEnd();
    blendAdd();
    gradientDisc(o.x, o.y, 5.0f, WHITE, 1.0f, ORB_COL, 0.6f, 16);
    midpointRing2D(o.x, o.y, 11 + 2 * bob, ORB_COL, 0.5f, 1.4f, 1.0f);   // [MIDPOINT]
}

static void drawParticles2D() {
    glowBegin();
    for (size_t i = 0; i < particles.size(); ++i) {
        const Particle& p = particles[i];
        float a = p.life / p.maxLife;
        glow2D(p.x, p.y, p.size * (0.5f + 0.5f * a), p.c, a);
    }
    glowEnd();
}

static void drawBoat2D() {
    if (state == ST_GAMEOVER) return;
    if (boat.invuln > 0 && boat.dashTimer <= 0 && state == ST_PLAYING && fmodf(animTime, 0.16f) < 0.08f) return;
    float lit = 0.35f + 0.65f * boat.light;

    glowBegin();
    glow2D(boat.x, boat.y, 44 + 50 * boat.light + 30 * boat.glowPulse, BOAT_GLOW,
           0.45f * lit + 0.3f * boat.glowPulse);
    glowEnd();

    float bob = sinf(animTime * 2.4f);
    float k = clampf(boat.dashTimer / 0.2f, 0, 1);
    glPushMatrix();
    glTranslatef(boat.x, boat.y + bob * 1.5f, 0);   // [TRANSFORM] translate
    glRotatef(boat.tilt + bob * 3.0f, 0, 0, 1);     //             rotate
    glScalef(1 - 0.18f * k, 1 + 0.35f * k, 1);      //             scale (dash stretch)

    static const float hull[6][2] = { { 0, 26 }, { 12, 11 }, { 11, -14 }, { 0, -23 }, { -11, -14 }, { -12, 11 } };
    Color paper = lerpC(rgb(0.45f, 0.5f, 0.6f), WHITE, lit);
    Color edge  = lerpC(rgb(0.15f, 0.22f, 0.35f), rgb(0.55f, 0.78f, 0.95f), lit);
    blendAlpha();
    glBegin(GL_TRIANGLE_FAN);                       // [INTERP] paper shading
    glColorC(paper, 1); glVertex2f(0, 0);
    glColorC(edge, 1);
    for (int i = 0; i <= 6; ++i) glVertex2f(hull[i % 6][0], hull[i % 6][1]);
    glEnd();
    glBegin(GL_TRIANGLES);                          // the folded sail ridge
    glColorC(WHITE * (0.6f + 0.4f * lit), 1); glVertex2f(0, 17);
    glColorC(paper * 0.8f, 1); glVertex2f(5.5f, -11); glVertex2f(-5.5f, -11);
    glEnd();
    blendAdd();
    glLineWidth(1.6f);
    glColorC(BOAT_GLOW, 0.8f * lit);
    glBegin(GL_LINE_LOOP);
    for (int i = 0; i < 6; ++i) glVertex2f(hull[i][0], hull[i][1]);
    glEnd();
    glColorC(BOAT_GLOW, 0.4f * lit);
    glBegin(GL_LINES);
    glVertex2f(0, 26); glVertex2f(0, 17);
    glVertex2f(0, -11); glVertex2f(0, -23);
    glEnd();
    glLineWidth(1.0f);
    glPopMatrix();
}

static void drawSky2D() {
    // mist hiding the far sea (objects emerge from it)
    blendAlpha();
    glBegin(GL_QUADS);
    glColorC(pal.horizon, 0.0f);  glVertex2f(0, HORIZON - 70); glVertex2f(W, HORIZON - 70);
    glColorC(pal.horizon, 0.95f); glVertex2f(W, HORIZON);      glVertex2f(0, HORIZON);
    glEnd();
    // [INTERP] sky gradient
    glBegin(GL_QUADS);
    glColorC(pal.horizon, 1); glVertex2f(0, HORIZON); glVertex2f(W, HORIZON);
    glColorC(pal.skyTop, 1);  glVertex2f(W, H);       glVertex2f(0, H);
    glEnd();

    // stars
    blendAdd();
    glPointSize(1.8f * vpScale);
    glBegin(GL_POINTS);
    for (size_t i = 0; i < skyStars.size(); ++i) {
        const BgStar& s = skyStars[i];
        float tw = 0.55f + 0.45f * sinf(animTime * 2.0f + s.phase);
        glColor4f(0.85f, 0.9f, 1.0f, s.bright * tw);
        glVertex2f(wrapf(s.x - travel * 0.01f, W), s.y);
    }
    glEnd();

    // aurora curtains appear once the score climbs
    float am = auroraAmount();
    if (am > 0) {
        for (int j = 0; j < 2; ++j) {
            Color c = j == 0 ? pal.glow : lerpC(pal.glow, rgb(0.8f, 0.3f, 1.0f), 0.6f);
            glBegin(GL_TRIANGLE_STRIP);
            for (float x = 0; x <= W; x += 20) {
                float yb = HORIZON + 14 + 10 * sinf(x * 0.010f + animTime * 0.4f + j * 2.0f);
                float yt = yb + 42 + 14 * sinf(x * 0.017f - animTime * 0.6f + j);
                float a  = 0.28f * am * (0.6f + 0.4f * sinf(x * 0.03f + animTime + j));
                glColorC(c, a); glVertex2f(x, yb);
                glColorC(c, 0); glVertex2f(x, yt);
            }
            glEnd();
        }
    }

    // moon
    glowBegin();
    glow2D(845, 655, 70, rgb(0.8f, 0.85f, 1.0f), 0.25f);
    glowEnd();
    blendAlpha();
    gradientDisc(845, 655, 13, rgb(1.0f, 1.0f, 0.95f), 1.0f, rgb(0.8f, 0.85f, 0.95f), 1.0f, 28);

    // glowing horizon line
    blendAdd();
    glBegin(GL_QUAD_STRIP);
    glColorC(pal.glow, 0.0f); glVertex2f(0, HORIZON - 4); glVertex2f(W, HORIZON - 4);
    glColorC(pal.glow, 0.35f + 0.4f * bloomFactor()); glVertex2f(0, HORIZON); glVertex2f(W, HORIZON);
    glColorC(pal.glow, 0.0f); glVertex2f(0, HORIZON + 5); glVertex2f(W, HORIZON + 5);
    glEnd();
}

static void drawShootingStar2D() {
    if (!star.active) return;
    blendAdd();
    // [DDA] tail: consecutive history points joined with DDA lines
    for (size_t i = 0; i + 1 < star.tail.size(); ++i) {
        float a0 = 1.0f - (float)i / star.tail.size();
        float a1 = 1.0f - (float)(i + 1) / star.tail.size();
        ddaLine2D(star.tail[i].x, star.tail[i].y, star.tail[i + 1].x, star.tail[i + 1].y,
                  GOLD, a0 * 0.9f, a1 * 0.9f, 3.0f);
    }
    glowBegin();
    for (size_t i = 0; i < star.tail.size(); i += 3) {
        float a = 1.0f - (float)i / star.tail.size();
        glow2D(star.tail[i].x, star.tail[i].y, 18 * a + 4, GOLD, 0.35f * a);
    }
    glow2D(star.x, star.y, 46, GOLD, 0.8f);
    glow2D(star.x, star.y, 14, WHITE, 1.0f);
    glowEnd();
    // rotating 4-point sparkle
    float rot = animTime * 4;
    glLineWidth(2.0f);
    glBegin(GL_LINES);
    for (int i = 0; i < 4; ++i) {
        float a = rot + i * PI / 2, l = (i % 2 ? 16.0f : 26.0f);
        glColorC(WHITE, 0.9f); glVertex2f(star.x, star.y);
        glColorC(GOLD, 0.0f);  glVertex2f(star.x + cosf(a) * l, star.y + sinf(a) * l);
    }
    glEnd();
    glLineWidth(1.0f);
}

static void render2D() {
    begin2D();
    float sx, sy;
    shakeOffset(sx, sy);
    glPushMatrix();
    glTranslatef(sx, sy, 0);                         // [TRANSFORM] screen shake

    drawSea2D();
    drawWaves2D();
    drawSpeedStreaks2D();
    drawRipples2D();
    drawTrail2D();
    for (size_t i = 0; i < obstacles.size(); ++i) {
        if (obstacles[i].y > HORIZON + 60) continue;
        if (obstacles[i].type == OBS_ROCK) drawRock2D(obstacles[i]);
        else                               drawJelly2D(obstacles[i]);
    }
    for (size_t i = 0; i < orbs.size(); ++i)
        if (orbs[i].y < HORIZON + 40) drawOrb2D(orbs[i]);
    drawParticles2D();
    drawBoat2D();

    float r, s;
    darknessParams(r, s);
    drawDarkness(boat.x, boat.y, r, s);

    drawSky2D();                 // drawn after the sea so it covers spawning objects
    drawShootingStar2D();        // the star stays visible through the darkness
    glPopMatrix();

    boatScreenX = boat.x; boatScreenY = boat.y; lightScreenR = r;
}

/* ============================================================================
 *  10. 3D VIEW  (chase camera over a rippling ocean)
 * ========================================================================== */

static inline float X3(float x) { return (x - W * 0.5f) * S3; }
static inline float Z3(float y) { return -y * S3; }

// Ocean height field: a sum of sines. `u` is fixed to the water (it moves
// with the scrolling world), so wave crests travel toward the camera.
static float waveH(float x, float z) {
    float u = -z + travel * S3;
    float t = animTime;
    return 0.45f * sinf(0.30f * u + 1.3f * t)
         + 0.25f * sinf(0.17f * x + 0.21f * u + 0.9f * t)
         + 0.12f * sinf(0.55f * x - 0.40f * u + 2.1f * t);
}

static float starHeight(float y) { return 1.2f + mxf(0.0f, y - 380.0f) * 0.022f; }

static void drawSkyBackdrop3D() {
    begin2D();
    blendAlpha();
    glBegin(GL_QUADS);
    glColorC(pal.horizon, 1); glVertex2f(0, 0); glVertex2f(W, 0);
    glVertex2f(W, H * 0.42f); glVertex2f(0, H * 0.42f);
    glColorC(pal.horizon, 1); glVertex2f(0, H * 0.42f); glVertex2f(W, H * 0.42f);
    glColorC(pal.skyTop, 1);  glVertex2f(W, H); glVertex2f(0, H);
    glEnd();
}

static void drawSkyDome3D(float ex, float ey, float ez) {
    glPushMatrix();
    glTranslatef(ex, ey, ez);       // sky objects follow the eye = infinitely far
    blendAdd();
    glPointSize(2.0f * vpScale);
    glBegin(GL_POINTS);
    for (size_t i = 0; i < domeStars.size(); ++i) {
        const DomeStar& s = domeStars[i];
        float tw = 0.55f + 0.45f * sinf(animTime * 2.0f + s.phase);
        glColor4f(0.85f, 0.9f, 1.0f, s.bright * tw);
        glVertex3f(s.x, s.y, s.z);
    }
    glEnd();

    // aurora curtains
    float am = auroraAmount();
    if (am > 0) {
        for (int j = 0; j < 2; ++j) {
            Color c = j == 0 ? pal.glow : lerpC(pal.glow, rgb(0.8f, 0.3f, 1.0f), 0.6f);
            float zc = -170.0f - j * 25.0f;
            glBegin(GL_TRIANGLE_STRIP);
            for (float x = -260; x <= 260; x += 10) {
                float yb = 14 + 8 * sinf(x * 0.02f + animTime * 0.3f + j * 2.0f);
                float yt = yb + 34 + 10 * sinf(x * 0.035f - animTime * 0.5f + j);
                float zz = zc + 20 * sinf(x * 0.012f + j);
                float a  = 0.30f * am * (0.6f + 0.4f * sinf(x * 0.05f + animTime + j));
                glColorC(c, a); glVertex3f(x, yb, zz);
                glColorC(c, 0); glVertex3f(x, yt, zz);
            }
            glEnd();
        }
    }

    // moon
    Vec3 md = norm3(v3(-0.45f, 0.32f, -1.0f));
    float mx = md.x * 190, my = md.y * 190, mz = md.z * 190;
    glowBegin();
    glow3D(mx, my, mz, 34, rgb(0.8f, 0.85f, 1.0f), 0.28f);
    glowEnd();
    blendAlpha();
    glBegin(GL_TRIANGLE_FAN);
    glColor4f(1.0f, 1.0f, 0.95f, 1.0f);
    glVertex3f(mx, my, mz);
    glColor4f(0.8f, 0.85f, 0.95f, 1.0f);
    for (int i = 0; i <= 28; ++i) {
        float a = i * 2 * PI / 28, c = cosf(a) * 6.5f, s = sinf(a) * 6.5f;
        glVertex3f(mx + camRight[0] * c + camUp[0] * s, my + camRight[1] * c + camUp[1] * s,
                   mz + camRight[2] * c + camUp[2] * s);
    }
    glEnd();
    glPopMatrix();
}

static std::vector<float> oceanH;
static std::vector<Color> oceanC;

// Rippling 3D ocean mesh with per-vertex colour: deep water, glowing crests,
// the boat's light pool, orb reflections and the starburst.  [INTERP]
static void drawOcean3D(int& nx, int& nz, float& x0, float& u0) {
    const float DX = 1.25f, DZ = 1.0f;
    nx = 80; nz = 62;
    x0 = floorf((camX - 50.0f) / DX) * DX;
    float uStart = -(camZ + 6.0f) + travel * S3;
    u0 = floorf(uStart / DZ) * DZ;          // grid locked to the water: no swimming
    oceanH.resize((nx + 1) * (nz + 1));
    oceanC.resize((nx + 1) * (nz + 1));

    bool alive = state == ST_PLAYING || state == ST_PAUSED || state == ST_START;
    float bx = X3(boat.x), bz = Z3(boat.y);
    float R = lightRadius() * S3;
    float lit = alive ? 0.2f + 0.8f * boat.light : 0.0f;
    float bf = bloomFactor();

    // only orbs/jellies near the visible strip contribute reflections
    std::vector<Vec3> glowSrc;   // x, z, type(0 orb / 1 jelly)
    for (size_t i = 0; i < orbs.size(); ++i)
        glowSrc.push_back(v3(X3(orbs[i].x), Z3(orbs[i].y), 0));
    for (size_t i = 0; i < obstacles.size(); ++i)
        if (obstacles[i].type == OBS_JELLY) glowSrc.push_back(v3(X3(obstacles[i].x), Z3(obstacles[i].y), 1));

    for (int j = 0; j <= nz; ++j) {
        float u = u0 + j * DZ;
        float z = travel * S3 - u;
        for (int i = 0; i <= nx; ++i) {
            float x = x0 + i * DX;
            float h = waveH(x, z);
            float crest = clampf((h - 0.12f) / 0.7f, 0, 1);
            Color c = pal.seaNear + pal.glow * (0.05f + 0.55f * crest * crest);
            float dxb = x - bx, dzb = z - bz;
            float li = lit * expf(-(dxb * dxb + dzb * dzb) / (R * R * 0.35f));
            c = c + pal.glow * (li * 0.7f) + WHITE * (li * 0.12f);
            for (size_t k = 0; k < glowSrc.size(); ++k) {
                float ddx = x - glowSrc[k].x, ddz = z - glowSrc[k].y;
                float d2 = ddx * ddx + ddz * ddz;
                if (d2 < 9.0f) c = c + (glowSrc[k].z > 0.5f ? JELLY_COL : ORB_COL) * (0.35f * expf(-d2 / 1.5f));
            }
            c = c + GOLD * (bf * (0.18f + 0.5f * crest));
            oceanH[j * (nx + 1) + i] = h;
            oceanC[j * (nx + 1) + i] = c;
        }
    }

    blendAlpha();
    for (int j = 0; j < nz; ++j) {
        float zA = travel * S3 - (u0 + j * DZ);
        float zB = travel * S3 - (u0 + (j + 1) * DZ);
        glBegin(GL_TRIANGLE_STRIP);
        for (int i = 0; i <= nx; ++i) {
            float x = x0 + i * DX;
            int a = j * (nx + 1) + i, b = (j + 1) * (nx + 1) + i;
            glColorC(oceanC[b], 1); glVertex3f(x, oceanH[b], zB);
            glColorC(oceanC[a], 1); glVertex3f(x, oceanH[a], zA);
        }
        glEnd();
    }
}

// neon lines along the wave crests (additive pass)
static void drawCrestLines3D(int nx, int nz, float x0, float u0) {
    const float DX = 1.25f, DZ = 1.0f;
    blendAdd();
    glLineWidth(1.5f);
    for (int j = 0; j <= nz; j += 1) {
        float z = travel * S3 - (u0 + j * DZ);
        glBegin(GL_LINE_STRIP);
        for (int i = 0; i <= nx; ++i) {
            float h = oceanH[j * (nx + 1) + i];
            float crest = clampf((h - 0.25f) / 0.55f, 0, 1);
            glColorC(lerpC(pal.glow, WHITE, 0.25f), crest * crest * 0.7f + bloomFactor() * crest * 0.4f);
            glVertex3f(x0 + i * DX, h + 0.03f, z);
        }
        glEnd();
    }
    glLineWidth(1.0f);
}

// flat-shaded triangle: brightness from a fixed light direction
static void shadedTri(Vec3 a, Vec3 b, Vec3 c, Color base, Vec3 L, float ambient) {
    Vec3 n = norm3(cross3(sub3(b, a), sub3(c, a)));
    float sh = ambient + (1.0f - ambient) * fabsf(dot3(n, L));
    glColorC(base * sh, 1);
    glVertex3f(a.x, a.y, a.z); glVertex3f(b.x, b.y, b.z); glVertex3f(c.x, c.y, c.z);
}

static void drawRock3D(const Obstacle& o) {
    float cx = X3(o.x), cz = Z3(o.y);
    float R = o.r * S3, hgt = R * 1.5f + 0.25f;
    float bx = X3(boat.x), bz = Z3(boat.y);
    bool alive = state == ST_PLAYING || state == ST_PAUSED;
    float d = sqrtf((cx - bx) * (cx - bx) + (cz - bz) * (cz - bz));
    float lit = clampf(1.0f - d / (lightRadius() * S3 + R), 0, 1) * (alive ? boat.light : 0) + bloomFactor() * 0.6f;
    lit = clampf(lit, 0, 1);

    Vec3 ring0[10], ring1[10];
    for (int i = 0; i < 10; ++i) {
        float a  = i * 2 * PI / 10;
        float a1 = a + PI / 10;
        ring0[i] = v3(cx + cosf(a) * R * o.shape[i] * 1.1f, -0.6f, cz + sinf(a) * R * o.shape[i] * 1.1f);
        ring1[i] = v3(cx + cosf(a1) * R * o.shape[(i + 3) % 10] * 0.7f, hgt * 0.55f,
                      cz + sinf(a1) * R * o.shape[(i + 3) % 10] * 0.7f);
    }
    Vec3 apex = v3(cx + R * 0.15f, hgt, cz - R * 0.1f);
    Vec3 L = norm3(v3(-0.4f, 0.8f, 0.5f));
    Color base = lerpC(rgb(0.10f, 0.11f, 0.15f), rgb(0.35f, 0.42f, 0.55f), lit);

    blendAlpha();
    glBegin(GL_TRIANGLES);
    for (int i = 0; i < 10; ++i) {
        int k = (i + 1) % 10;
        shadedTri(ring0[i], ring0[k], ring1[i], base, L, 0.3f);
        shadedTri(ring0[k], ring1[k], ring1[i], base, L, 0.3f);
        shadedTri(ring1[i], ring1[k], apex, base * 1.1f, L, 0.3f);
    }
    glEnd();
}

static void drawRockFoam3D(const Obstacle& o) {
    midpointCircle((int)(o.r * 1.05f + 8 + 2 * sinf(animTime * 2 + o.phase)), circBuf);
    glColorC(pal.glow, 0.4f);
    glBegin(GL_POINTS);
    for (size_t i = 0; i < circBuf.size(); i += 2) {
        float x = X3(o.x + circBuf[i].x), z = Z3(o.y + circBuf[i].y);
        glVertex3f(x, waveH(x, z) + 0.05f, z);
    }
    glEnd();
}

static void drawJelly3D(const Obstacle& o) {
    float cx = X3(o.x), cz = Z3(o.y);
    float R  = o.r * S3 * 1.1f;
    float p  = sinf(animTime * 3.2f + o.phase);
    float cy = 0.75f + 0.18f * sinf(animTime * 1.7f + o.phase);
    float rx = R * (1 + 0.12f * p), ry = R * 0.85f * (1 - 0.12f * p);

    blendAdd();
    // tentacles
    glLineWidth(1.5f);
    for (int k = 0; k < 7; ++k) {
        float ang = k * 2 * PI / 7;
        glBegin(GL_LINE_STRIP);
        for (int s = 0; s <= 8; ++s) {
            float t = s / 8.0f;
            float wig = sinf(animTime * 4 + k + s * 0.8f) * 0.08f * s;
            glColorC(JELLY_COL, 0.8f * (1 - t));
            glVertex3f(cx + cosf(ang) * rx * 0.75f + wig, cy - t * 1.4f, cz + sinf(ang) * rx * 0.75f);
        }
        glEnd();
    }
    glLineWidth(1.0f);
    // bell: hemisphere rings, whitish top -> pink rim [INTERP]
    const int LAT = 6, LON = 16;
    for (int la = 0; la < LAT; ++la) {
        float a0 = la * (PI / 2) / LAT, a1 = (la + 1) * (PI / 2) / LAT;
        glBegin(GL_TRIANGLE_STRIP);
        for (int lo = 0; lo <= LON; ++lo) {
            float b = lo * 2 * PI / LON;
            for (int e = 0; e < 2; ++e) {
                float a = e == 0 ? a0 : a1;
                float t = a / (PI / 2);
                glColorC(lerpC(JELLY_COL, rgb(1.0f, 0.9f, 0.97f), t), 0.30f + 0.25f * t);
                glVertex3f(cx + cosf(b) * cosf(a) * rx, cy + sinf(a) * ry, cz + sinf(b) * cosf(a) * rx);
            }
        }
        glEnd();
    }
    glowBegin();
    glow3D(cx, cy + ry * 0.3f, cz, R * 3.0f, JELLY_COL, 0.35f + 0.1f * p);
    glowEnd();
}

// Classic folded paper boat built from flat-shaded triangles.
static void drawBoat3D() {
    if (state == ST_GAMEOVER) return;
    if (boat.invuln > 0 && boat.dashTimer <= 0 && state == ST_PLAYING && fmodf(animTime, 0.16f) < 0.08f) return;
    float bx = X3(boat.x), bz = Z3(boat.y);
    float wh = waveH(bx, bz);
    float pitch = atanf((waveH(bx, bz - 0.7f) - waveH(bx, bz + 0.7f)) / 1.4f) * 180.0f / PI;
    float roll  = atanf((waveH(bx + 0.7f, bz) - waveH(bx - 0.7f, bz)) / 1.4f) * 180.0f / PI;
    float k = clampf(boat.dashTimer / 0.2f, 0, 1);
    float lit = 0.35f + 0.65f * boat.light;

    glPushMatrix();
    glTranslatef(bx, wh + 0.12f, bz);                 // [TRANSFORM] 3D translate
    glRotatef(boat.tilt * 0.8f, 0, 1, 0);             // yaw while steering
    glRotatef(pitch, 1, 0, 0);                        // ride the waves
    glRotatef(-roll + boat.tilt * 0.9f, 0, 0, 1);     // bank
    glScalef(1 - 0.1f * k, 1, 1 + 0.4f * k);          // dash stretch

    Vec3 bowT = v3(0, 0.55f, -1.45f), stnT = v3(0, 0.55f, 1.45f);
    Vec3 lF = v3(-0.55f, 0.5f, -0.75f), lB = v3(-0.55f, 0.5f, 0.75f);
    Vec3 rF = v3( 0.55f, 0.5f, -0.75f), rB = v3( 0.55f, 0.5f, 0.75f);
    Vec3 kF = v3(0, -0.1f, -0.8f), kB = v3(0, -0.1f, 0.8f);
    Vec3 sLF = v3(-0.12f, 0.5f, -0.8f), sLB = v3(-0.12f, 0.5f, 0.8f);
    Vec3 sRF = v3( 0.12f, 0.5f, -0.8f), sRB = v3( 0.12f, 0.5f, 0.8f);
    Vec3 apex = v3(0, 1.45f, 0);
    Vec3 L = norm3(v3(0.3f, 0.9f, 0.35f));
    Color paper = lerpC(rgb(0.4f, 0.45f, 0.55f), WHITE, lit);
    Color inner = paper * 0.6f;

    glEnable(GL_POLYGON_OFFSET_FILL);
    glPolygonOffset(1.0f, 1.0f);
    blendAlpha();
    glBegin(GL_TRIANGLES);
    // left hull
    shadedTri(bowT, lF, kF, paper, L, 0.45f);
    shadedTri(lF, lB, kB, paper, L, 0.45f);
    shadedTri(lF, kB, kF, paper, L, 0.45f);
    shadedTri(lB, stnT, kB, paper, L, 0.45f);
    // right hull
    shadedTri(bowT, rF, kF, paper, L, 0.45f);
    shadedTri(rF, rB, kB, paper, L, 0.45f);
    shadedTri(rF, kB, kF, paper, L, 0.45f);
    shadedTri(rB, stnT, kB, paper, L, 0.45f);
    // inside of the boat
    shadedTri(lF, rF, rB, inner, L, 0.7f);
    shadedTri(lF, rB, lB, inner, L, 0.7f);
    shadedTri(bowT, rF, lF, inner, L, 0.7f);
    shadedTri(stnT, lB, rB, inner, L, 0.7f);
    // sail
    shadedTri(sLF, sLB, apex, paper, L, 0.5f);
    shadedTri(sRF, sRB, apex, paper, L, 0.5f);
    shadedTri(sLF, sRF, apex, paper * 0.9f, L, 0.5f);
    shadedTri(sLB, sRB, apex, paper * 0.9f, L, 0.5f);
    glEnd();
    glDisable(GL_POLYGON_OFFSET_FILL);

    // neon edges
    blendAdd();
    glLineWidth(1.8f);
    glColorC(BOAT_GLOW, 0.85f * lit);
    Vec3 rim[7] = { bowT, lF, lB, stnT, rB, rF, bowT };
    glBegin(GL_LINE_STRIP);
    for (int i = 0; i < 7; ++i) glVertex3f(rim[i].x, rim[i].y, rim[i].z);
    glEnd();
    glBegin(GL_LINES);
    Vec3 e[] = { bowT, kF, kF, kB, kB, stnT, sLF, apex, sLB, apex, sRF, apex, sRB, apex };
    for (int i = 0; i < 14; ++i) glVertex3f(e[i].x, e[i].y, e[i].z);
    glEnd();
    glLineWidth(1.0f);
    glPopMatrix();

    glowBegin();
    glow3D(bx, wh + 0.6f, bz, 2.6f + 2.4f * boat.light + 1.5f * boat.glowPulse, BOAT_GLOW,
           0.35f * lit + 0.25f * boat.glowPulse);
    glowEnd();
}

static void drawTrail3D() {
    if (trail.size() < 2) return;
    Color cc = comboColor();
    float boost = 0.5f + mnf((float)combo, 20.0f) * 0.035f + bloomFactor() * 0.5f;
    float wScale = 1.0f + mnf((float)combo, 20.0f) * 0.03f;
    blendAdd();
    for (int side = -1; side <= 1; side += 2) {
        glBegin(GL_TRIANGLE_STRIP);
        for (size_t i = 0; i < trail.size(); ++i) {
            const TrailPt& p = trail[i];
            float x = X3(p.x), z = Z3(p.y);
            float h = waveH(x, z) + 0.08f;
            float w = (0.2f + 0.45f * p.life) * wScale;
            glColorC(cc, p.life * 0.5f * boost); glVertex3f(x, h, z);
            glColorC(cc, 0);                     glVertex3f(x + side * w, h, z);
        }
        glEnd();
    }
}

static void drawRipples3D() {
    blendAdd();
    glPointSize(2.5f * vpScale);
    for (size_t i = 0; i < ripples.size(); ++i) {
        const Ripple& r = ripples[i];
        midpointCircle((int)(r.r + 0.5f), circBuf);           // [MIDPOINT] in 3D
        glColorC(r.c, 0.6f * r.life / r.maxLife);
        glBegin(GL_POINTS);
        size_t step = circBuf.size() > 600 ? 3 : (circBuf.size() > 200 ? 2 : 1);
        for (size_t k = 0; k < circBuf.size(); k += step) {
            float x = X3(r.x + circBuf[k].x), z = Z3(r.y + circBuf[k].y);
            glVertex3f(x, waveH(x, z) + 0.06f, z);
        }
        glEnd();
    }
}

static void drawOrbs3D() {
    glowBegin();
    for (size_t i = 0; i < orbs.size(); ++i) {
        const Orb& o = orbs[i];
        float x = X3(o.x), z = Z3(o.y);
        float y = 0.75f + 0.2f * sinf(animTime * 3 + o.phase);
        glow3D(x, y, z, 1.6f, ORB_COL, 0.55f);
        glow3D(x, y, z, 0.6f, ORB_COL, 0.9f);
        glow3D(x, y, z, 0.25f, WHITE, 1.0f);
    }
    glowEnd();
}

static void drawParticles3D() {
    glowBegin();
    for (size_t i = 0; i < particles.size(); ++i) {
        const Particle& p = particles[i];
        float a = p.life / p.maxLife;
        glow3D(X3(p.x), 0.2f + p.h, Z3(p.y), p.size * S3 * (0.6f + 0.6f * a), p.c, a);
    }
    glowEnd();
}

static void drawShootingStar3D() {
    if (!star.active) return;
    blendAdd();
    glLineWidth(3.0f);
    glBegin(GL_LINE_STRIP);
    for (size_t i = 0; i < star.tail.size(); ++i) {
        float a = 1.0f - (float)i / star.tail.size();
        glColorC(GOLD, a);
        glVertex3f(X3(star.tail[i].x), starHeight(star.tail[i].y), Z3(star.tail[i].y));
    }
    glEnd();
    glLineWidth(1.0f);
    glowBegin();
    for (size_t i = 0; i < star.tail.size(); i += 2) {
        float a = 1.0f - (float)i / star.tail.size();
        glow3D(X3(star.tail[i].x), starHeight(star.tail[i].y), Z3(star.tail[i].y), 0.9f * a + 0.2f, GOLD, 0.4f * a);
    }
    float sx = X3(star.x), sy = starHeight(star.y), sz = Z3(star.y);
    glow3D(sx, sy, sz, 2.8f, GOLD, 0.85f);
    glow3D(sx, sy, sz, 0.8f, WHITE, 1.0f);
    glowEnd();
}

static void render3D() {
    drawSkyBackdrop3D();

    // [PROJECTION] perspective camera
    glMatrixMode(GL_PROJECTION);
    glLoadIdentity();
    gluPerspective(55.0, W / H, 0.1, 400.0);
    glMatrixMode(GL_MODELVIEW);
    glLoadIdentity();
    float sx, sy;
    shakeOffset(sx, sy);
    float bx = X3(boat.x);
    float ex = camX + sx * 0.03f, ey = 5.0f + sy * 0.03f, ez = camZ;
    gluLookAt(ex, ey, ez, camX * 0.9f + bx * 0.1f, 0.4f, camZ - 26.0f, 0, 1, 0);

    glGetDoublev(GL_MODELVIEW_MATRIX, mvSaved);
    glGetDoublev(GL_PROJECTION_MATRIX, projSaved);
    glGetIntegerv(GL_VIEWPORT, viewSaved);
    float m[16];
    glGetFloatv(GL_MODELVIEW_MATRIX, m);
    camRight[0] = m[0]; camRight[1] = m[4]; camRight[2] = m[8];
    camUp[0]    = m[1]; camUp[1]    = m[5]; camUp[2]    = m[9];

    glDisable(GL_DEPTH_TEST);
    drawSkyDome3D(ex, ey, ez);

    // [FOG] linear fog blends distant geometry into the horizon colour
    glEnable(GL_DEPTH_TEST);
    glDepthMask(GL_TRUE);
    glEnable(GL_FOG);
    GLfloat fogCol[4] = { pal.horizon.r, pal.horizon.g, pal.horizon.b, 1.0f };
    glFogi(GL_FOG_MODE, GL_LINEAR);
    glFogfv(GL_FOG_COLOR, fogCol);
    glFogf(GL_FOG_START, 14.0f);
    glFogf(GL_FOG_END, 50.0f);

    int nx, nz; float x0, u0;
    drawOcean3D(nx, nz, x0, u0);
    for (size_t i = 0; i < obstacles.size(); ++i)
        if (obstacles[i].type == OBS_ROCK) drawRock3D(obstacles[i]);
    drawBoat3D();

    // additive pass: no depth writes, fog fades glows to black (not to fog colour)
    glDepthMask(GL_FALSE);
    GLfloat black[4] = { 0, 0, 0, 1 };
    glFogfv(GL_FOG_COLOR, black);
    drawCrestLines3D(nx, nz, x0, u0);
    drawRipples3D();
    glPointSize(2.0f * vpScale);
    blendAdd();
    for (size_t i = 0; i < obstacles.size(); ++i)
        if (obstacles[i].type == OBS_ROCK) drawRockFoam3D(obstacles[i]);
    drawTrail3D();
    drawOrbs3D();
    for (size_t i = 0; i < obstacles.size(); ++i)
        if (obstacles[i].type == OBS_JELLY) drawJelly3D(obstacles[i]);
    drawParticles3D();
    glDisable(GL_FOG);
    drawShootingStar3D();
    glDepthMask(GL_TRUE);
    glDisable(GL_DEPTH_TEST);

    // where is the boat (and its light radius) on screen? -> darkness overlay
    GLdouble wx, wy, wz, wx2, wy2, wz2;
    float bz = Z3(boat.y), wh = waveH(bx, bz);
    gluProject(bx, wh + 0.3, bz, mvSaved, projSaved, viewSaved, &wx, &wy, &wz);
    float r, s;
    darknessParams(r, s);
    gluProject(bx + r * S3, wh + 0.3, bz, mvSaved, projSaved, viewSaved, &wx2, &wy2, &wz2);
    boatScreenX = (float)((wx - vpX) / vpScale);
    boatScreenY = (float)((wy - vpY) / vpScale);
    lightScreenR = (float)(fabs(wx2 - wx) / vpScale) * 1.25f;
    begin2D();
    drawDarkness(boatScreenX, boatScreenY, lightScreenR, s * 0.85f);
}

/* ============================================================================
 *  11. HUD AND SCREENS
 * ========================================================================== */

static bool worldToScreen(float x, float y, float& sx, float& sy) {
    if (!mode3D) { sx = x; sy = y; return true; }
    GLdouble wx, wy, wz;
    gluProject(X3(x), 1.2, Z3(y), mvSaved, projSaved, viewSaved, &wx, &wy, &wz);
    if (wz > 1.0) return false;
    sx = (float)((wx - vpX) / vpScale);
    sy = (float)((wy - vpY) / vpScale);
    return true;
}

static void drawPopups() {
    for (size_t i = 0; i < popups.size(); ++i) {
        const Popup& p = popups[i];
        float sx, sy;
        if (!worldToScreen(p.x, p.y, sx, sy)) continue;
        float a = clampf(p.life / 0.5f, 0, 1);
        sx = clampf(sx, 60, W - 60); sy = clampf(sy, 20, H - 90);
        bitmapTextCentered(sx, sy, p.text.c_str(), GLUT_BITMAP_HELVETICA_12, p.c, a);
    }
}

static void drawHUD() {
    char buf[96];
    blendAlpha();
    glDisable(GL_TEXTURE_2D);

    // score / best
    snprintf(buf, sizeof buf, "SCORE  %d", (int)score);
    glColor4f(0.85f, 0.95f, 1.0f, 0.95f);
    bitmapText(24, H - 34, buf, GLUT_BITMAP_HELVETICA_18);
    snprintf(buf, sizeof buf, "BEST  %d", (int)mxf(highScore, score));
    glColor4f(0.6f, 0.75f, 0.9f, 0.8f);
    bitmapText(24, H - 54, buf, GLUT_BITMAP_HELVETICA_12);

    // combo
    if (combo > 0) {
        snprintf(buf, sizeof buf, "COMBO %d   x%d", combo, multiplier());
        strokeText(W * 0.5f, H - 46, 22.0f * (1.0f + 0.25f * comboPulse), buf, comboColor(), 1.0f, true);
        float frac = clampf(comboTimer / COMBO_WINDOW, 0, 1);
        blendAdd();
        glBegin(GL_QUADS);                              // combo window bar
        glColorC(comboColor(), 0.7f);
        glVertex2f(W * 0.5f - 90 * frac, H - 56); glVertex2f(W * 0.5f + 90 * frac, H - 56);
        glVertex2f(W * 0.5f + 90 * frac, H - 53); glVertex2f(W * 0.5f - 90 * frac, H - 53);
        glEnd();
    }
    if (bloomTimer > 0) {
        snprintf(buf, sizeof buf, "STARBURST  %.1fs", bloomTimer);
        strokeText(W * 0.5f, H - 82, 16.0f, buf, GOLD, 0.6f + 0.4f * sinf(animTime * 8), true);
    }

    // light bar with colour interpolated red -> amber -> cyan  [INTERP]
    float bx = W - 270, by = H - 42, bw = 190, bh = 14;
    glColor4f(0.7f, 0.85f, 1.0f, 0.8f);
    bitmapText(bx, by + 20, "LIGHT", GLUT_BITMAP_HELVETICA_10);
    blendAlpha();
    glColor4f(0.0f, 0.0f, 0.0f, 0.45f);
    glBegin(GL_QUADS);
    glVertex2f(bx, by); glVertex2f(bx + bw, by); glVertex2f(bx + bw, by + bh); glVertex2f(bx, by + bh);
    glEnd();
    Color red = rgb(1.0f, 0.2f, 0.25f), amber = rgb(1.0f, 0.7f, 0.2f), cyan = rgb(0.3f, 0.95f, 1.0f);
    float L = clampf(boat.light, 0, 1);
    blendAdd();
    glBegin(GL_QUAD_STRIP);
    for (int i = 0; i <= 20; ++i) {
        float t = (i / 20.0f) * L;
        Color c = t < 0.5f ? lerpC(red, amber, t * 2) : lerpC(amber, cyan, (t - 0.5f) * 2);
        if (bloomTimer > 0) c = lerpC(c, GOLD, 0.7f);
        glColorC(c, 0.9f);
        glVertex2f(bx + t * bw, by); glVertex2f(bx + t * bw, by + bh);
    }
    glEnd();
    // [DDA] bar frame
    Color frame = (L < 0.25f && fmodf(animTime, 0.5f) < 0.25f && state == ST_PLAYING) ? red : rgb(0.6f, 0.8f, 1.0f);
    ddaLine2D(bx - 2, by - 2, bx + bw + 2, by - 2, frame, 0.7f, 0.7f, 1.2f);
    ddaLine2D(bx - 2, by + bh + 2, bx + bw + 2, by + bh + 2, frame, 0.7f, 0.7f, 1.2f);
    ddaLine2D(bx - 2, by - 2, bx - 2, by + bh + 2, frame, 0.7f, 0.7f, 1.2f);
    ddaLine2D(bx + bw + 2, by - 2, bx + bw + 2, by + bh + 2, frame, 0.7f, 0.7f, 1.2f);
    if (L < 0.25f && state == ST_PLAYING && fmodf(animTime, 0.6f) < 0.4f) {
        glColorC(red, 0.95f);
        bitmapText(bx + bw - 62, by - 18, "LOW LIGHT", GLUT_BITMAP_HELVETICA_10);
    }

    // [MIDPOINT] dash gauge: dim full ring + bright arc for readiness
    float gx = W - 40, gy = H - 35;
    float ready = clampf(1.0f - boat.dashCooldown / 1.1f, 0, 1);
    blendAdd();
    midpointRing2D(gx, gy, 14, rgb(0.5f, 0.7f, 1.0f), 0.25f, 1.6f, 1.0f);
    midpointRing2D(gx, gy, 14, ready >= 1 ? WHITE : BOAT_GLOW, ready >= 1 ? 0.95f : 0.7f, 2.0f, ready);
    if (ready >= 1) {
        glowBegin(); glow2D(gx, gy, 16, BOAT_GLOW, 0.5f); glowEnd();
    }
    bitmapTextCentered(gx, gy - 4, "D", GLUT_BITMAP_HELVETICA_10, WHITE, 0.8f);
    bitmapTextCentered(gx, gy - 30, "DASH", GLUT_BITMAP_HELVETICA_10, rgb(0.7f, 0.85f, 1.0f), 0.8f);

    // footer
    glColor4f(0.55f, 0.7f, 0.85f, 0.65f);
    bitmapText(24, 14, "ARROWS/WASD move   SPACE dash   P pause   V switch view", GLUT_BITMAP_HELVETICA_10);
    const char* mv = mode3D ? "VIEW: 3D CHASE" : "VIEW: 2D TOP";
    glColor4f(0.55f, 0.7f, 0.85f, 0.65f);
    bitmapText(W - 24 - bitmapWidth(mv, GLUT_BITMAP_HELVETICA_10), 14, mv, GLUT_BITMAP_HELVETICA_10);
}

static void dimScreen(float a) {
    blendAlpha();
    glColor4f(0.0f, 0.0f, 0.02f, a);
    glBegin(GL_QUADS);
    glVertex2f(0, 0); glVertex2f(W, 0); glVertex2f(W, H); glVertex2f(0, H);
    glEnd();
}

static void drawStartScreen() {
    dimScreen(0.25f);
    float pulse = 0.85f + 0.15f * sinf(animTime * 2.0f);
    strokeText(W * 0.5f, 505, 62, "STARLIT TIDES", lerpC(pal.glow, WHITE, 0.2f), pulse, true);
    bitmapTextCentered(W * 0.5f, 470, "a bioluminescent night voyage", GLUT_BITMAP_HELVETICA_18,
                       rgb(0.7f, 0.85f, 1.0f), 0.9f);

    const char* lines[] = {
        "ARROW KEYS / WASD  -  steer your paper boat",
        "SPACE  -  dash (brief invincibility)",
        "Collect glowing plankton to keep your light alive",
        "Dodge rocks & jellyfish  -  near misses build your combo",
        "Catch the golden shooting star for a STARBURST",
    };
    for (int i = 0; i < 5; ++i)
        bitmapTextCentered(W * 0.5f, 410 - i * 25.0f, lines[i], GLUT_BITMAP_HELVETICA_12,
                           rgb(0.8f, 0.9f, 1.0f), 0.85f);

    if (fmodf(animTime, 1.2f) < 0.85f)
        strokeText(W * 0.5f, 245, 22, "PRESS ENTER TO SET SAIL", GOLD, 1.0f, true);
    char buf[64];
    snprintf(buf, sizeof buf, "BEST  %d", (int)highScore);
    bitmapTextCentered(W * 0.5f, 212, buf, GLUT_BITMAP_HELVETICA_12, rgb(0.7f, 0.8f, 0.95f), 0.85f);
    const char* mv = mode3D ? "V: view mode (now 3D CHASE)    ESC: quit"
                            : "V: view mode (now 2D TOP)    ESC: quit";
    bitmapTextCentered(W * 0.5f, 30, mv, GLUT_BITMAP_HELVETICA_10, rgb(0.6f, 0.75f, 0.9f), 0.75f);
}

static void drawGameOverScreen() {
    float a = smooth01(overTimer / 0.8f);
    blendAlpha();
    glColor4f(0.0f, 0.01f, 0.04f, 0.6f * a);
    glBegin(GL_QUADS);
    glVertex2f(200, 170); glVertex2f(800, 170); glVertex2f(800, 540); glVertex2f(200, 540);
    glEnd();
    blendAdd();
    glLineWidth(1.5f);
    glColorC(pal.glow, 0.5f * a);
    glBegin(GL_LINE_LOOP);
    glVertex2f(200, 170); glVertex2f(800, 170); glVertex2f(800, 540); glVertex2f(200, 540);
    glEnd();
    glLineWidth(1.0f);

    strokeText(W * 0.5f, 470, 46, "DARKNESS FALLS", rgb(0.55f, 0.7f, 1.0f), a, true);
    char buf[128];
    snprintf(buf, sizeof buf, "SCORE  %d", (int)score);
    strokeText(W * 0.5f, 405, 30, buf, WHITE, a, true);
    if (newBest) {
        if (fmodf(animTime, 0.8f) < 0.55f) strokeText(W * 0.5f, 362, 20, "NEW BEST!", GOLD, a, true);
    } else {
        snprintf(buf, sizeof buf, "BEST  %d", (int)highScore);
        bitmapTextCentered(W * 0.5f, 368, buf, GLUT_BITMAP_HELVETICA_18, rgb(0.7f, 0.8f, 0.95f), a);
    }
    int secs = (int)elapsed;
    snprintf(buf, sizeof buf, "Orbs %d     Max combo %d     Near misses %d     Time %d:%02d",
             orbsCollected, maxCombo, nearMisses, secs / 60, secs % 60);
    bitmapTextCentered(W * 0.5f, 315, buf, GLUT_BITMAP_HELVETICA_12, rgb(0.75f, 0.85f, 1.0f), a);
    if (overTimer > 0.8f) {
        if (fmodf(animTime, 1.2f) < 0.85f)
            strokeText(W * 0.5f, 245, 20, "PRESS ENTER TO SAIL AGAIN", GOLD, 1.0f, true);
        bitmapTextCentered(W * 0.5f, 205, "ESC to quit", GLUT_BITMAP_HELVETICA_12, rgb(0.6f, 0.7f, 0.85f), 0.8f);
    }
}

static void drawOverlay() {
    begin2D();
    glDisable(GL_TEXTURE_2D);

    // full-screen flash on hits / starburst [BLEND]
    if (flashAlpha > 0) {
        blendAdd();
        glColorC(flashColor, flashAlpha * 0.6f);
        glBegin(GL_QUADS);
        glVertex2f(0, 0); glVertex2f(W, 0); glVertex2f(W, H); glVertex2f(0, H);
        glEnd();
    }

    if (state == ST_PLAYING || state == ST_PAUSED || state == ST_DYING) {
        drawPopups();
        drawHUD();
    }
    if (state == ST_START)    drawStartScreen();
    if (state == ST_GAMEOVER) drawGameOverScreen();
    if (state == ST_PAUSED) {
        dimScreen(0.5f);
        strokeText(W * 0.5f, 360, 48, "PAUSED", pal.glow, 1.0f, true);
        bitmapTextCentered(W * 0.5f, 320, "press P to resume", GLUT_BITMAP_HELVETICA_12, WHITE, 0.8f);
    }
    if (bannerTimer > 0) {
        float a = clampf(bannerTimer / 0.5f, 0, 1);
        strokeText(W * 0.5f, 150, 20, bannerText.c_str(), WHITE, a, true);
    }
}

/* ============================================================================
 *  12. GLUT CALLBACKS
 * ========================================================================== */

static void display() {
    glViewport(0, 0, winW, winH);
    glClearColor(0, 0, 0, 1);
    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);
    glViewport(vpX, vpY, vpW, vpH);                    // letterboxed 10:7

    if (mode3D) render3D(); else render2D();
    drawOverlay();

    glutSwapBuffers();
}

static void reshape(int w, int h) {
    winW = w; winH = h > 0 ? h : 1;
    float aspect = W / H;
    if ((float)winW / winH > aspect) {
        vpH = winH; vpW = (int)(winH * aspect);
        vpX = (winW - vpW) / 2; vpY = 0;
    } else {
        vpW = winW; vpH = (int)(winW / aspect);
        vpX = 0; vpY = (winH - vpH) / 2;
    }
    vpScale = vpW / W;
}

// [TIMER] ~60 FPS fixed callback; motion uses real elapsed time (dt)
static void timer(int) {
    int now = glutGet(GLUT_ELAPSED_TIME);
    float dt = (now - lastTicks) / 1000.0f;
    lastTicks = now;
    if (dt > 0.05f) dt = 0.05f;
    if (dt < 0.0f)  dt = 0.0f;
    update(dt);
    glutPostRedisplay();
    glutTimerFunc(16, timer, 0);
}

static void toggleView() {
    mode3D = !mode3D;
    showBanner(mode3D ? "3D CHASE VIEW" : "2D TOP VIEW");
}

static void setDirKey(unsigned char key, bool down) {
    switch (key) {
    case 'a': case 'A': keyL = down; break;
    case 'd': case 'D': keyR = down; break;
    case 'w': case 'W': keyU = down; break;
    case 's': case 'S': keyD = down; break;
    default: break;
    }
}

static void keyboard(unsigned char key, int, int) {
    setDirKey(key, true);
    switch (key) {
    case 27:
        if (score > highScore) { highScore = score; saveHighScore(); }
        exit(0);
    case 13: case '\n':
        if (state == ST_START) resetGame();
        else if (state == ST_GAMEOVER && overTimer > 0.8f) resetGame();
        break;
    case ' ':
        if (state == ST_PLAYING) dashRequest = true;
        else if (state == ST_START) resetGame();
        else if (state == ST_GAMEOVER && overTimer > 0.8f) resetGame();
        break;
    case 'p': case 'P':
        if (state == ST_PLAYING) state = ST_PAUSED;
        else if (state == ST_PAUSED) state = ST_PLAYING;
        break;
    case 'v': case 'V': case 'c': case 'C':
        toggleView();
        break;
    default: break;
    }
}

static void keyboardUp(unsigned char key, int, int) { setDirKey(key, false); }

static void special(int key, int, int) {
    switch (key) {
    case GLUT_KEY_LEFT:  keyL = true; break;
    case GLUT_KEY_RIGHT: keyR = true; break;
    case GLUT_KEY_UP:    keyU = true; break;
    case GLUT_KEY_DOWN:  keyD = true; break;
    default: break;
    }
}

static void specialUp(int key, int, int) {
    switch (key) {
    case GLUT_KEY_LEFT:  keyL = false; break;
    case GLUT_KEY_RIGHT: keyR = false; break;
    case GLUT_KEY_UP:    keyU = false; break;
    case GLUT_KEY_DOWN:  keyD = false; break;
    default: break;
    }
}

static void initScene() {
    for (int i = 0; i < 140; ++i) {
        BgStar s = { frand(0, W), frand(0, HORIZON), frand(0, 2 * PI), frand(0.3f, 1.0f) };
        seaStars.push_back(s);
    }
    for (int i = 0; i < 110; ++i) {
        float y = HORIZON + 6 + powf(frand(0, 1), 0.7f) * (H - HORIZON - 6);
        BgStar s = { frand(0, W), y, frand(0, 2 * PI), frand(0.3f, 1.0f) };
        skyStars.push_back(s);
    }
    for (int i = 0; i < 700; ++i) {
        float th = frand(0, 2 * PI);
        float y  = frand(0.03f, 1.0f);
        float rr = sqrtf(1 - y * y);
        DomeStar s = { cosf(th) * rr * 200, y * 200, sinf(th) * rr * 200, frand(0, 2 * PI), frand(0.25f, 1.0f) };
        domeStars.push_back(s);
    }
}

static void initGL() {
    glEnable(GL_BLEND);
    blendAlpha();
    glEnable(GL_LINE_SMOOTH);
    glHint(GL_LINE_SMOOTH_HINT, GL_NICEST);
    glEnable(GL_POINT_SMOOTH);
    glHint(GL_POINT_SMOOTH_HINT, GL_NICEST);
    glShadeModel(GL_SMOOTH);                         // Gouraud colour interpolation
    glDepthFunc(GL_LEQUAL);
    makeGlowTexture();
}

int main(int argc, char** argv) {
    srand((unsigned)time(NULL));
    glutInit(&argc, argv);
    glutInitDisplayMode(GLUT_DOUBLE | GLUT_RGBA | GLUT_DEPTH);
    glutInitWindowSize(1000, 700);
    glutInitWindowPosition(80, 60);
    glutCreateWindow("Starlit Tides");

    initGL();
    initScene();
    loadHighScore();
    resetBoat();
    star.active = false;

    glutDisplayFunc(display);
    glutReshapeFunc(reshape);
    glutKeyboardFunc(keyboard);
    glutKeyboardUpFunc(keyboardUp);
    glutSpecialFunc(special);
    glutSpecialUpFunc(specialUp);
    glutIgnoreKeyRepeat(1);
    lastTicks = glutGet(GLUT_ELAPSED_TIME);
    glutTimerFunc(16, timer, 0);

    glutMainLoop();
    return 0;
}
