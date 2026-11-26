// =============================================================================
//  Database connection (Neon serverless Postgres over HTTP)
// -----------------------------------------------------------------------------
//  The Neon integration in the Vercel Marketplace adds DATABASE_URL (and
//  POSTGRES_URL) to the project's environment variables automatically.
//  Never put the connection string in code or commit a .env file.
// =============================================================================
import { neon } from '@neondatabase/serverless';
import { createHash } from 'node:crypto';

const url = process.env.DATABASE_URL || process.env.POSTGRES_URL;

/** Tagged-template SQL client, e.g.  await sql`SELECT 1`  (values are parameterised, no SQL injection). */
export const sql = url ? neon(url) : null;

let schemaReady = null;

/** Creates the table and index on first use (safe to call many times). */
export function ensureSchema() {
  if (!sql) throw new Error('DATABASE_URL is not set. Connect the Neon database to this Vercel project.');
  if (!schemaReady) {
    schemaReady = (async () => {
      await sql`
        CREATE TABLE IF NOT EXISTS scores (
          id          BIGSERIAL PRIMARY KEY,
          name        VARCHAR(16)  NOT NULL,
          score       INTEGER      NOT NULL CHECK (score >= 0),
          orbs        INTEGER      NOT NULL DEFAULT 0,
          distance    INTEGER      NOT NULL DEFAULT 0,
          duration    INTEGER      NOT NULL DEFAULT 0,
          version     VARCHAR(16)  NOT NULL DEFAULT 'unknown',
          client_hash CHAR(16)     NOT NULL DEFAULT '',
          created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
        )`;
      await sql`CREATE INDEX IF NOT EXISTS scores_score_idx ON scores (score DESC, created_at ASC)`;
      await sql`CREATE INDEX IF NOT EXISTS scores_client_time_idx ON scores (client_hash, created_at)`;
    })().catch((err) => {
      schemaReady = null; // retry on the next request
      throw err;
    });
  }
  return schemaReady;
}

/**
 * One-way hash of the caller's IP, used only for rate limiting.
 * The raw IP is never stored.
 */
export function clientHash(req) {
  const fwd = req.headers['x-forwarded-for'];
  const ip = (Array.isArray(fwd) ? fwd[0] : fwd || '').split(',')[0].trim() || req.socket?.remoteAddress || 'unknown';
  const salt = process.env.RATE_LIMIT_SALT || 'starlit-tides';
  return createHash('sha256').update(salt + ip).digest('hex').slice(0, 16);
}
