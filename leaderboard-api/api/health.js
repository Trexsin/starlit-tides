// GET /api/health -> { ok: true, database: true }   (quick check that the deployment and Neon connection work)
import { sql, ensureSchema } from '../lib/db.js';

export default async function handler(req, res) {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Cache-Control', 'no-store');
  try {
    await ensureSchema();
    const [{ total }] = await sql`SELECT count(*)::int AS total FROM scores`;
    return res.status(200).json({ ok: true, database: true, scores: total });
  } catch (err) {
    console.error('[health]', err);
    return res.status(503).json({ ok: false, database: false, error: sql ? 'Database query failed.' : 'DATABASE_URL is not set.' });
  }
}
