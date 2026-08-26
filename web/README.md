# TrueMain Web

The public Nuxt 4 frontend of TrueMain, served at [truemain.lol](https://truemain.lol). It lives in the
`web/` directory of the monorepo; the architecture, the deploy story and the rest of the stack are in the
[root `README.md`](../README.md).

The browser never reaches the backend directly: the Nitro server proxies `/api/**` to the Api service at
`NUXT_API_BASE_URL` (required — `http://api:8080` inside the Docker network, `http://localhost:8080` when
running the Api on the host).

## Running it

The supported dev path is the Docker stack from the repository root, which starts Postgres, the Api and this
app with hot reload:

```bash
docker compose -f compose.dev.yaml up
```

The site is then on `http://localhost:3000`.

To run only the Nuxt dev server (against an Api you started yourself):

```bash
npm ci
NUXT_API_BASE_URL=http://localhost:8080 npm run dev
```

## Commands

```bash
npm run dev         # dev server
npm run build       # production build — what CI runs, and the only thing that catches stale-type errors
npm run preview     # serve the production build locally
npm run typecheck   # can pass on a stale .nuxt; trust `build`
npm run test        # Vitest
```

Use npm, not pnpm/yarn/bun: `package-lock.json` is committed and CI installs from it. Regenerate it with
`npx npm@11.13.0` — CI's version, and older npm drops sharp's optional dependencies.

## Conventions

- `docs/DESIGN_SYSTEM.md` — tokens, surfaces, typography. Every token is rendered on one screen at
  `/dev/design-system`; the other `/dev/*` pages are component playgrounds, all stripped from production
  builds by a `pages:extend` hook in `nuxt.config.ts`.
- `.claude/docs/features.md` at the repo root — what each page already ships.
