// =============================================================================
//  /api/scores - the global leaderboard
// -----------------------------------------------------------------------------
//  GET  /api/scores?limit=10
//       -> { scores: [ { rank, name, score, orbs, distance, created_at } ] }
//
//  POST /api/scores   (Content-Type: application/json)
//       { "name": "Mira", "score": 4210, "orbs": 37, "distance": 1650,
//         "duration": 142, "version": "1.0" }
//       -> 201 { id, rank, score }
//
//  Rate limit: 10 submissions per 10 minutes per client (hashed IP).
//  CORS is open so a WebGL build of the game can call it too.
// =============================================================================
import { sql, ensureSchema, clientHash } from '../lib/db.js';
import { validateSubmission, parseLimit } from '../lib/validate.js';

const RATE_LIMIT = 10;
const RATE_WINDOW_MINUTES = 10;

function cors(res) {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type');
}

export default async function handler(req, res) {
  cors(res);
  if (req.method === 'OPTIONS') return res.status(204).end();

  try {
    await ensureSchema();

    if (req.method === 'GET') {
      const limit = parseLimit(req.query?.limit);
      const rows = await sql`
        SELECT name, score, orbs, distance, created_at
        FROM scores
        ORDER BY score DESC, created_at ASC
        LIMIT ${limit}`;
      res.setHeader('Cache-Control', 's-maxage=10, stale-while-revalidate=30');
      return res.status(200).json({ scores: rows.map((r, i) => ({ rank: i + 1, ...r })) });
    }

    if (req.method === 'POST') {
      let body = req.body;
      if (typeof body === 'string') {
        try { body = JSON.parse(body); } catch { return res.status(400).json({ error: 'Body must be valid JSON.' }); }
      }
      const check = validateSubmission(body);
      if (!check.ok) return res.status(400).json({ error: check.error });

      const hash = clientHash(req);
      const [{ recent }] = await sql`
        SELECT count(*)::int AS recent FROM scores
        WHERE client_hash = ${hash} AND created_at > now() - make_interval(mins => ${RATE_WINDOW_MINUTES})`;
      if (recent >= RATE_LIMIT) return res.status(429).json({ error: 'Too many submissions, try again in a few minutes.' });

      const v = check.value;
      const [row] = await sql`
        INSERT INTO scores (name, score, orbs, distance, duration, version, client_hash)
        VALUES (${v.name}, ${v.score}, ${v.orbs}, ${v.distance}, ${v.duration}, ${v.version}, ${hash})
        RETURNING id, score, created_at`;
      const [{ rank }] = await sql`
        SELECT count(*)::int + 1 AS rank FROM scores
        WHERE score > ${row.score} OR (score = ${row.score} AND created_at < ${row.created_at})`;
      return res.status(201).json({ id: Number(row.id), rank, score: row.score });
    }

    res.setHeader('Allow', 'GET, POST, OPTIONS');
    return res.status(405).json({ error: 'Method not allowed.' });
  } catch (err) {
    console.error('[scores]', err);
    return res.status(500).json({ error: 'Leaderboard is unavailable right now.' });
  }
}
