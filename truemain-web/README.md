# TrueMain Web

TrueMain Web is the Nuxt-based frontend for the TrueMain platform. It is developed and run as part of the TrueMain monorepo and communicates with backend services via a configurable API base URL.

## Monorepo integration

This project lives under the `truemain-web/` directory of the TrueMain monorepo.

- Frontend code: this directory (`truemain-web/`)
- Shared configuration / tooling: managed at the monorepo root
- Backend / API services: defined in other packages within the monorepo

TrueMain Web expects a running API service reachable via `NUXT_PUBLIC_API_BASE_URL`. When running the full monorepo (e.g. via Docker), that variable should point at the API gateway or main backend service exposed by the compose stack.

## Environment variables

The application reads configuration from standard Nuxt environment variables. The key variable for API communication is:

- `NUXT_PUBLIC_API_BASE_URL` (required)  
  Base URL for all HTTP requests from the frontend to the backend.  
  Examples:
  - Local Docker compose: `http://api:8080` (service name and port defined in `docker-compose.yml`)
  - Local non-Docker development: `http://localhost:8080`
  - Staging/production: the public URL of the API gateway (e.g. `https://api.staging.truemain.example`, `https://api.truemain.example`)

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

After the stack is up, TrueMain Web should be available at the host and port configured in the compose file (commonly `http://localhost:3000`). The `NUXT_PUBLIC_API_BASE_URL` is usually wired through as an environment variable for the `truemain-web` service in `docker-compose.yml`.

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

Make sure `NUXT_PUBLIC_API_BASE_URL` is set in your environment before starting the dev server.

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
