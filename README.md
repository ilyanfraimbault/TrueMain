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

### Reverse proxy & HTTPS (prod)

En prod, tout le trafic public passe par **Caddy**, qui termine le TLS et obtient/renouvelle automatiquement les certificats Let's Encrypt (cf. [`Caddyfile`](Caddyfile)) :

- Site public : `https://truemain.lol` (et `www` → redirigé vers l'apex)
- Dashboard admin : `https://admin.truemain.lol` — login via `ADMIN_USERNAME` / `ADMIN_PASSWORD`

`web` et `admin` ne sont plus publiés sur l'hôte : seul Caddy les atteint via le réseau interne `truemain_internal`. En prod le service `admin` est lancé avec `NUXT_SESSION_COOKIE_SECURE=true` et `NUXT_TRUST_PROXY=true` : le cookie de session est marqué `Secure` (accepté par le navigateur uniquement sur HTTPS, fourni par Caddy) et le throttle de login fait confiance au `X-Forwarded-For` posé par Caddy.

**Prérequis côté serveur :**

1. Enregistrements DNS `A` pointant vers l'IP publique du VPS (en DNS direct, **pas** en proxy Cloudflare, sinon le challenge Let's Encrypt échoue) :
   ```
   truemain.lol        A   <IP_du_VPS>
   www.truemain.lol    A   <IP_du_VPS>
   admin.truemain.lol  A   <IP_du_VPS>
   ```
2. Ports 80 **et** 443 ouverts (challenges HTTP-01 / TLS-ALPN-01) :
   ```bash
   ufw allow 80,443/tcp
   ```
3. Le [`Caddyfile`](Caddyfile) doit être présent à côté de `compose.prod.yaml` sur le serveur (monté en lecture seule dans le conteneur `caddy`).

> Mongo et Postgres ne sont pas publiés (réseau interne uniquement). Les anciens ports `3000`/`3001` ne sont plus exposés — tu peux fermer les règles firewall correspondantes.

### Accès au dashboard admin (qa)

La stack **qa** reste publiée directement sur l'hôte sans TLS : `http://<hôte>:3002` (login `ADMIN_USERNAME` / `ADMIN_PASSWORD`). Identifiants et cookie en clair — ne l'expose pas sans restreindre le port par firewall :
```bash
ufw allow from <IP_de_confiance> to any port 3002
```
(Passer la qa derrière le même Caddy est un suivi possible si elle partage l'hôte de la prod.)

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
- `ADMIN_USERNAME`, `ADMIN_PASSWORD` — login du dashboard admin. **Définir un vrai `ADMIN_PASSWORD` est obligatoire** (seule barrière devant les outils d'ops) ; `.env.example` met volontairement `REPLACE_ME`
- `ADMIN_SESSION_PASSWORD` — scelle le cookie de session admin (32+ caractères aléatoires, ex. `openssl rand -hex 32`)
- `NUXT_SESSION_COOKIE_SECURE`, `NUXT_TRUST_PROXY` — réglés par environnement dans les fichiers compose (pas via `.env`) : `"true"` en prod (admin derrière Caddy/TLS) → cookie `Secure` + confiance au `X-Forwarded-For` pour le throttle de login ; `false` en qa/local (exposition HTTP directe)
- `PGADMIN_DEFAULT_EMAIL`, `PGADMIN_DEFAULT_PASSWORD`
- `NUXT_API_BASE_URL` (côté web, généralement `http://api:8080` dans Docker)

## Architecture

Le diagramme d'architecture est dans [`docs/diagrams/architecture.drawio`](docs/diagrams/architecture.drawio) — à ouvrir avec [draw.io](https://app.diagrams.net/) ou l'extension VS Code.

Les RFCs détaillant les phases majeures :

- [`docs/phase-5-data-split-rfc.md`](docs/phase-5-data-split-rfc.md)
- [`docs/phase-5-runes-rfc.md`](docs/phase-5-runes-rfc.md)
- [`docs/phase-6-pattern-junction-rfc.md`](docs/phase-6-pattern-junction-rfc.md)
- [`docs/refactor-option-c.md`](docs/refactor-option-c.md) — backlog refactors lourds
