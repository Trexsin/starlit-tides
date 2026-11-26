-- Starlit Tides leaderboard schema (Neon Postgres).
-- The API creates this automatically on first request (lib/db.js -> ensureSchema),
-- so you only need this file to inspect or recreate the table by hand
-- in the Neon SQL Editor.

CREATE TABLE IF NOT EXISTS scores (
  id          BIGSERIAL PRIMARY KEY,
  name        VARCHAR(16)  NOT NULL,
  score       INTEGER      NOT NULL CHECK (score >= 0),
  orbs        INTEGER      NOT NULL DEFAULT 0,
  distance    INTEGER      NOT NULL DEFAULT 0,
  duration    INTEGER      NOT NULL DEFAULT 0,
  version     VARCHAR(16)  NOT NULL DEFAULT 'unknown',
  client_hash CHAR(16)     NOT NULL DEFAULT '',   -- salted hash of the IP, for rate limiting only
  created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS scores_score_idx       ON scores (score DESC, created_at ASC);
CREATE INDEX IF NOT EXISTS scores_client_time_idx ON scores (client_hash, created_at);

-- Handy queries:
-- SELECT name, score, orbs, distance, created_at FROM scores ORDER BY score DESC LIMIT 10;
-- DELETE FROM scores WHERE name = 'cheater';
