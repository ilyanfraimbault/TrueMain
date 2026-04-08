# Environnement QA
Le stack QA reproduit la prod avec des services dédiés, une base isolée, une clé Riot différente et `pgAdmin` inclus.
## Fichiers
- `compose.qa.yaml`
- `.env.qa.example`
- `pgadmin/servers.qa.json`
## Démarrage
```bash
cp .env.qa.example .env.qa
docker compose --env-file .env.qa -f compose.qa.yaml up -d
```
## Accès
- API QA : `http://localhost:8081`
- Web QA : `http://localhost:3001`
- pgAdmin QA : `http://localhost:5051`
## pgAdmin
Les identifiants pgAdmin viennent de `.env.qa` : `PGADMIN_DEFAULT_EMAIL` et `PGADMIN_DEFAULT_PASSWORD`.
La connexion PostgreSQL QA est préconfigurée dans `pgadmin/servers.qa.json`.
## Remarque
Si le QA est exposé derrière un domaine ou un reverse proxy, adaptez `NUXT_PUBLIC_API_BASE_URL` dans `.env.qa`.
