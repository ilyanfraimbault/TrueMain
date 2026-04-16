# TrueMain Web

TrueMain Web is the Nuxt-based frontend for the TrueMain platform. It is developed and run as part of the TrueMain monorepo and communicates with backend services through a server-side Nuxt proxy.

## Monorepo integration

This project lives under the `truemain-web/` directory of the TrueMain monorepo.

- Frontend code: this directory (`truemain-web/`)
- Shared configuration / tooling: managed at the monorepo root
- Backend / API services: defined in other packages within the monorepo

TrueMain Web expects a running API service reachable from the `web` container via `NUXT_API_BASE_URL`. Browser requests go only to the Nuxt app, which proxies `/api/**` to the backend service.

## Environment variables

The application reads configuration from standard Nuxt environment variables. The key variable for backend communication is:

- `NUXT_API_BASE_URL` (required)  
  Base URL used by the Nuxt server proxy to reach the backend API.  
  Examples:
  - Local Docker compose: `http://api:8080` (service name and port defined in `docker-compose.yml`)
  - Local non-Docker development: `http://localhost:8080`
  - Staging/production: the internal URL reachable by the Nuxt server process

Set this variable in your environment before starting the app (for example in a `.env` file at the project root or via your container configuration).

## Development using Docker (recommended)

In the monorepo, the recommended way to run TrueMain Web for development is via Docker and Docker Compose from the repository root. This ensures that the frontend and all required backend services run with a consistent configuration.

Typical workflow from the monorepo root:

```bash
# Build and start all services (including TrueMain Web)
docker compose up --build

# Or, if a specific profile/stack is defined for web-only development:
# docker compose --profile web up --build
```

After the stack is up, TrueMain Web should be available at the host and port configured in the compose file (commonly `http://localhost:3000`). The `NUXT_API_BASE_URL` is usually wired through as an environment variable for the `truemain-web` service in `docker-compose.yml`.

Refer to the monorepo’s root `README.md` and `docker-compose` files for the authoritative Docker commands and available profiles.

## Local development without Docker

You can also run the Nuxt dev server directly from this directory if you have Node.js installed.

### Setup

Install dependencies:

```bash
# npm
npm install

# pnpm
pnpm install

# yarn
yarn install

# bun
bun install
```

Make sure `NUXT_API_BASE_URL` is set in your environment before starting the dev server.

### Development server

Start the development server (default: `http://localhost:3000`):

```bash
# npm
npm run dev

# pnpm
pnpm dev

# yarn
yarn dev

# bun
bun run dev
```

## Production

Build the application for production:

```bash
# npm
npm run build

# pnpm
pnpm build

# yarn
yarn build

# bun
bun run build
```

Locally preview the production build:

```bash
# npm
npm run preview

# pnpm
pnpm preview

# yarn
yarn preview

# bun
bun run preview
```

For deployment options and advanced configuration, see the [Nuxt deployment documentation](https://nuxt.com/docs/getting-started/deployment) and the TrueMain monorepo documentation.
