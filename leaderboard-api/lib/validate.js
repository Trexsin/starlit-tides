// =============================================================================
//  Score validation - pure functions, no database (unit-tested in test/)
// -----------------------------------------------------------------------------
//  A game client can never keep a secret, so anyone could POST a fake score.
//  We can't stop a determined cheater, but these checks reject garbage and
//  obviously impossible runs using the game's own rules:
//    * max world speed is 16 m/s  -> distance <= duration * 17
//    * score = distance points + orb points (x multipliers) + bonuses
//      -> score must fit under a generous ceiling derived from those
// =============================================================================

export const LIMITS = {
  nameMin: 1,
  nameMax: 16,
  maxScore: 10_000_000,
  maxOrbs: 20_000,
  maxDistance: 200_000,     // metres
  maxDuration: 3 * 60 * 60, // seconds (3 hours)
  maxSpeed: 17,             // m/s, a little above the game's 16 m/s cap
  pointsPerOrb: 50,
  maxMultiplier: 12,        // combo (up to x6) x Starburst (x2)
  bonusAllowance: 100_000,  // near misses, events
};

const NAME_ALLOWED = /^[A-Za-z0-9 _.\-]+$/;

/** Trims, collapses spaces and checks the player name. Returns null if invalid. */
export function cleanName(raw) {
  if (typeof raw !== 'string') return null;
  const name = raw.normalize('NFKC').replace(/\s+/g, ' ').trim();
  if (name.length < LIMITS.nameMin || name.length > LIMITS.nameMax) return null;
  if (!NAME_ALLOWED.test(name)) return null;
  return name;
}

function toInt(v) {
  const n = typeof v === 'string' && v.trim() !== '' ? Number(v) : v;
  return Number.isFinite(n) ? Math.floor(n) : NaN;
}

/**
 * Validates a submitted run.
 * @returns {{ ok: true, value: object } | { ok: false, error: string }}
 */
export function validateSubmission(body) {
  if (!body || typeof body !== 'object') return { ok: false, error: 'Body must be a JSON object.' };

  const name = cleanName(body.name);
  if (!name) return { ok: false, error: `Name must be ${LIMITS.nameMin}-${LIMITS.nameMax} letters, numbers, spaces, _ . or -.` };

  const score = toInt(body.score);
  const orbs = toInt(body.orbs ?? 0);
  const distance = toInt(body.distance ?? 0);
  const duration = toInt(body.duration ?? 0);
  const version = typeof body.version === 'string' ? body.version.slice(0, 16) : 'unknown';

  if (!Number.isInteger(score) || score < 0 || score > LIMITS.maxScore) return { ok: false, error: 'Invalid score.' };
  if (!Number.isInteger(orbs) || orbs < 0 || orbs > LIMITS.maxOrbs) return { ok: false, error: 'Invalid orb count.' };
  if (!Number.isInteger(distance) || distance < 0 || distance > LIMITS.maxDistance) return { ok: false, error: 'Invalid distance.' };
  if (!Number.isInteger(duration) || duration < 1 || duration > LIMITS.maxDuration) return { ok: false, error: 'Invalid duration.' };

  // plausibility checks based on the game's rules
  if (distance > duration * LIMITS.maxSpeed) return { ok: false, error: 'Run is faster than the game allows.' };
  const ceiling = distance * 2 + orbs * LIMITS.pointsPerOrb * LIMITS.maxMultiplier + LIMITS.bonusAllowance;
  if (score > ceiling) return { ok: false, error: 'Score is higher than this run allows.' };

  return { ok: true, value: { name, score, orbs, distance, duration, version } };
}

/** Clamps ?limit= to 1..50 (default 10). */
export function parseLimit(raw) {
  const n = toInt(raw ?? 10);
  if (!Number.isInteger(n)) return 10;
  return Math.min(50, Math.max(1, n));
}
