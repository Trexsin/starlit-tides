# Starlit Tides: Leaderboard API

**Live:** <https://starlit-tides.vercel.app/api/scores> · health check: <https://starlit-tides.vercel.app/api/health>

A tiny global leaderboard running as **Vercel serverless functions** with a **Neon Postgres** database.

```
Unity game ──HTTPS──► Vercel  /api/scores ──SQL──► Neon Postgres
```

The game never connects to the database directly. A database password inside a game build could be extracted by anyone, so only the Vercel functions hold the connection string, as an environment variable.

## Endpoints

| Method | Path | Body / query | Response |
|---|---|---|---|
| `GET` | `/api/scores?limit=10` | `limit` 1–50 | `{ "scores": [ { "rank", "name", "score", "orbs", "distance", "created_at" } ] }` |
| `POST` | `/api/scores` | `{ "name", "score", "orbs", "distance", "duration", "version" }` | `201 { "id", "rank", "score" }` |
| `GET` | `/api/health` | | `{ "ok": true, "database": true, "scores": 42 }` |

**Validation** (`lib/validate.js`, unit-tested):
- Names are 1–16 letters, numbers, spaces, `_` `.` or `-`.
- Runs faster than the game's top speed are rejected.
- Scores above what the run's distance and orbs could earn are rejected.

**Rate limit:** 10 submissions per 10 minutes per client. Clients are identified by a salted hash of their IP, and the raw IP is never stored.

The table is created automatically on the first request (see `sql/schema.sql`).

## Deploy (one time)

1. **Import the repo in Vercel:** <https://vercel.com/new> → pick the GitHub repo.
   - **Root Directory:** `leaderboard-api`  ← important
   - Framework preset: **Other**. Leave the build settings empty.
2. **Add the database:** in the Vercel project → **Storage** → **Create Database** → **Neon** (Serverless Postgres) → free plan → **Connect** to this project.
   This adds `DATABASE_URL` (and related variables) to the project automatically.
3. **(Optional)** Project → Settings → Environment Variables → add `RATE_LIMIT_SALT` with any random string.
4. **Redeploy** (Deployments → ⋯ → Redeploy) so the functions see the new variables.
5. Open `https://<your-project>.vercel.app/api/health`. It should show `"database": true`.

After this, every `git push` to the main branch redeploys automatically.

## Try it

```bash
curl https://starlit-tides.vercel.app/api/health
```

```bash
curl -X POST https://starlit-tides.vercel.app/api/scores -H "Content-Type: application/json" -d '{"name":"Tester","score":1200,"orbs":12,"distance":500,"duration":60,"version":"1.0"}'
```

```bash
curl "https://starlit-tides.vercel.app/api/scores?limit=10"
```

## Local development

```bash
npm install
```

```bash
npm test
```

```bash
vercel env pull .env.local && vercel dev
```

`.env*` files are git-ignored. Never commit the real `DATABASE_URL`.

## Use it from Unity

Copy `Online/_StarlitTides/Scripts/Online/LeaderboardClient.cs` into the Unity project. Add the component to the Managers object and set **Base Url** to your deployment URL. Then:

```csharp
LeaderboardClient.Instance.SubmitScore("Mira", (ok, rank, error) => Debug.Log(ok ? $"Rank #{rank}" : error));
LeaderboardClient.Instance.FetchTop(10, (ok, entries, error) => { /* fill the Game Over screen */ });
```

Phase 5 (Bonus 11) wires this into the Game Over screen.

> **About cheating:** a game client can't keep secrets, so a determined player could still post a fake score. The validation stops garbage and impossible runs, which is the right level for a class project. You can delete bad entries in the Neon SQL Editor: `DELETE FROM scores WHERE id = …;`
