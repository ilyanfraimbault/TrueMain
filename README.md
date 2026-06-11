# TrueMain

TrueMain ingère les données League of Legends de la Riot API, agrège ce que jouent les *vrais mains* (joueurs aux ratios games / mastery élevés sur un champion), et expose ces agrégats à un frontend Nuxt qui les présente comme un guide par champion.

## Structure du repo

```
TrueMain/
├── backend/                 # solution .NET (Api, Core, Data, Ingestor, tests)
│   ├── Api/                 # ASP.NET Core, surface HTTP
│   ├── Core/                # types LoL communs (PlatformId, RegionalRoute, …)
│   ├── Data/                # DbContext, migrations EF, repositories
│   ├── Ingestor/            # worker .NET qui appelle Riot et persiste
│   ├── tests/               # UnitTests, IntegrationTests, TestKit
│   ├── TrueMain.sln
│   ├── Directory.Build.props
│   └── Directory.Packages.props
├── web/                     # frontend Nuxt 4
│   ├── app/
│   ├── server/              # proxy Nitro vers l'API
│   ├── nuxt.config.ts
│   └── package.json
├── compose.yaml             # stack production locale (images build)
├── compose.dev.yaml         # stack dev avec dotnet watch + Vite HMR
├── compose.qa.yaml          # stack QA (images ghcr)
├── compose.prod.yaml        # stack prod (images ghcr)
├── docs/
│   ├── diagrams/            # architecture.drawio (draw.io)
│   └── *.md                 # RFCs et roadmaps
├── .github/                 # workflows CI/CD + dependabot
└── README.md
```

## Démarrage rapide

### Dev (hot-reload backend + frontend)

Prérequis : Docker, un fichier `.env` à la racine (cf. `.env.example`).

```bash
docker compose -f compose.dev.yaml up --build --watch
```

- API : http://localhost:8080
- Web : http://localhost:3000
- pgAdmin : http://localhost:5050

### Production (build local)

```bash
docker compose -f compose.yaml up --build
```

### Stacks QA / prod (images publiées sur GHCR)

```bash
docker compose -f compose.qa.yaml up
# ou
docker compose -f compose.prod.yaml up
```

### Accès au dashboard admin (qa / prod)

Le dashboard d'administration est publié **directement sur l'hôte**, sans reverse proxy — qa : `http://<hôte>:3002`, prod : `http://<hôte>:3001`. Login via `ADMIN_USERNAME` / `ADMIN_PASSWORD`.

> ⚠️ **Pas de TLS dans ce mode.** Les identifiants et le cookie de session transitent **en clair (HTTP)**. Ne l'exposez pas sur l'Internet public sans, a minima, restreindre le port par firewall :
> ```bash
> ufw allow from <IP_de_confiance> to any port 3001   # prod (3002 en qa)
> ```
> Mongo et Postgres ne sont pas publiés (réseau interne `truemain_internal` uniquement). Lorsqu'un domaine sera disponible, le mode recommandé est de repasser l'admin en `127.0.0.1:<port>:3000` derrière un reverse proxy qui termine le TLS.

## Build / test séparé

### Backend .NET

```bash
cd backend
dotnet restore TrueMain.sln
dotnet build TrueMain.sln --configuration Release
dotnet test tests/TrueMain.UnitTests/TrueMain.UnitTests.csproj
dotnet test tests/TrueMain.IntegrationTests/TrueMain.IntegrationTests.csproj
```

### Frontend Nuxt

```bash
cd web
npm ci
npm run typecheck
npm run dev      # serveur de dev sur http://localhost:3000
npm run build    # production build dans .output/
```

## Variables d'environnement

Toutes les valeurs nécessaires sont listées dans `.env.example` (dev/prod) et `.env.qa.example` (QA). Au minimum :

- `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`
- `RIOT_API_KEY` — clé Riot Developer (`RGAPI-…`)
- `OPS_API_KEY` — clé pour `/ops/*` (32+ caractères)
- `MONGO_USER`, `MONGO_PASSWORD` — credentials MongoDB (logs + audit ; alphanumériques, ils entrent dans l'URI de connexion)
- `ADMIN_USERNAME`, `ADMIN_PASSWORD` — login du dashboard admin (défaut `truemain` / `truemain`)
- `ADMIN_SESSION_PASSWORD` — scelle le cookie de session admin (32+ caractères aléatoires, ex. `openssl rand -hex 32`)
- `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_DEFAULT_PASSWORD`
- `NUXT_API_BASE_URL` (côté web, généralement `http://api:8080` dans Docker)

## Architecture

Le diagramme d'architecture est dans [`docs/diagrams/architecture.drawio`](docs/diagrams/architecture.drawio) — à ouvrir avec [draw.io](https://app.diagrams.net/) ou l'extension VS Code.

Les RFCs détaillant les phases majeures :

- [`docs/phase-5-data-split-rfc.md`](docs/phase-5-data-split-rfc.md)
- [`docs/phase-5-runes-rfc.md`](docs/phase-5-runes-rfc.md)
- [`docs/phase-6-pattern-junction-rfc.md`](docs/phase-6-pattern-junction-rfc.md)
- [`docs/refactor-option-c.md`](docs/refactor-option-c.md) — backlog refactors lourds
