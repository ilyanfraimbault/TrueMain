# API TrueMain — référence des endpoints

Surface HTTP exposée par `backend/Api` (ASP.NET Core). Ce document liste tous
les endpoints, leurs paramètres d'entrée et la forme exacte de ce qu'ils
renvoient.

> Généré à partir des contrôleurs (`backend/Api/Controllers`) et des read models
> (`backend/Api/ReadModels`). En dev, la doc OpenAPI vivante est aussi servie sur
> `/openapi/v1.json` et l'UI Scalar sur `/scalar/v1` (Development uniquement).

## Conventions générales

- **Base URL** : `http://localhost:8080` en dev (cf. `compose.dev.yaml`). En prod,
  derrière Caddy. Aucun préfixe global (`/api`) — les routes sont à la racine.
- **Format** : JSON. Les noms de propriétés passent en **camelCase** sur le fil
  (politique `System.Text.Json` web par défaut). Les exemples ci-dessous sont
  donc en camelCase, même si les read models C# sont en PascalCase.
- **Erreurs** : toutes les erreurs (4xx/5xx) arrivent en
  [ProblemDetails RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) :
  ```json
  {
    "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    "title": "Bad Request",
    "status": 400,
    "detail": "position must be one of TOP, JUNGLE, MIDDLE, BOTTOM, UTILITY."
  }
  ```
- **Rate limiting** : 100 requêtes / minute / IP sur toute la surface publique.
  Au-delà → `429 Too Many Requests`. Les health checks en sont exemptés.
- **Authentification** : seuls les endpoints `/ops/*` sont protégés. Ils exigent
  l'en-tête `X-Ops-Key: <OPS_API_KEY>`. Sans clé valide → `401 Unauthorized`.
  Le reste (`/champions/*`, `/truemains/*`) est public.
- **Patch** : un paramètre `patch` accepte la forme Riot complète (`16.4.521`)
  ou abrégée ; il est normalisé en `major.minor` (`16.4`). Une valeur invalide
  est traitée comme « non filtré » (patch courant / tous patchs selon l'endpoint).
- **Position** : valeurs Riot canoniques `TOP`, `JUNGLE`, `MIDDLE`, `BOTTOM`,
  `UTILITY` (insensible à la casse en entrée, ex. `mid` → `MIDDLE`). Sur les
  endpoints où `position` est **requis**, une valeur non reconnue renvoie `400`.
- **Pagination** : les endpoints paginés prennent `page` (1-based) et `pageSize`.
  Convention « 0 = défaut » : un `pageSize`/`limit` omis ou ≤ 0 applique la taille
  par défaut du service, et les valeurs au-delà du plafond sont ramenées au cap.

## Endpoints d'infrastructure

| Méthode | Chemin            | Auth | Description |
|---------|-------------------|------|-------------|
| GET     | `/healthz`        | —    | Liveness. `200` si le process tourne (aucun check). |
| GET     | `/readyz`         | —    | Readiness. Vérifie la connexion Postgres (tag `ready`). `200`/`503`. |
| GET     | `/openapi/v1.json`| —    | Document OpenAPI (Development uniquement). |
| GET     | `/scalar/v1`      | —    | UI de référence Scalar (Development uniquement). |

---

# Champions — `/champions`

Public. Agrégats par champion calculés sur la population de *truemains*. Tous ces
endpoints renvoient `429` sous rate-limit.

## `GET /champions`

Annuaire des champions : une ligne par couple `(champion, position)` pour un patch.

**Query**

| Param   | Type   | Requis | Défaut        | Description |
|---------|--------|--------|---------------|-------------|
| `patch` | string | non    | dernier patch | Filtre patch (`16.4`). Omis → dernier patch global. |

**Réponse `200`** — `ChampionSummaryReadModel[]`

```json
[
  {
    "championId": 103,
    "games": 1840,
    "wins": 981,
    "winRate": 0.5332,
    "pickRate": 0.072,
    "lanePlayRate": 0.86,
    "trueMainCount": 47,
    "banRate": 0.184,
    "tier": "A",
    "position": "MIDDLE",
    "patchVersion": "16.4",
    "lastUpdatedAtUtc": "2026-06-25T03:11:00Z",
    "topBuild": {
      "firstItemId": 6653,
      "primaryKeystoneId": 8214,
      "secondaryStyleId": 8100,
      "itemPath": [6653, 3020, 3157, 3089]
    }
  }
]
```

- `pickRate` : part des games truemains sur cette position prises par ce champion.
- `lanePlayRate` : répartition des lanes du champion (0.86 = 86 % de ses games ici).
- `banRate` : part des matchs observés où le champion a été banni (#920). Indépendant
  de la lane : identique sur toutes les lignes d'un champion. **`null` = non observé**,
  pas « jamais banni » — les bans ne sont collectés que depuis le déploiement de #920 et
  ne sont pas rattrapables, donc les patchs antérieurs valent `null`. Dénominateur
  différent de `pickRate` (tous les matchs observés vs games des mains suivis) : les deux
  **ne s'additionnent pas**, il n'y a donc pas de « presence ». N'entre pas dans `tier`.
- `tier` : `S`/`A`/`B`/`C`/`D`, percentile relatif au patch courant.
- `topBuild` : build dominant (résumé) ; `null` si aucun pattern observé.

## `GET /champions/{championId}`

Page champion : onglets de build dominants pour un `(patch, position)`.

**Path** — `championId` (int)

**Query**

| Param      | Type   | Requis | Description |
|------------|--------|--------|-------------|
| `patch`    | string | non    | Filtre patch. |
| `position` | string | non    | Filtre position. |

**Réponse `200`** — `ChampionResponse` · **`404`** si le champion n'a pas de scope.

```json
{
  "championId": 103,
  "patch": "16.4",
  "position": "MIDDLE",
  "totalGames": 1840,
  "totalWins": 981,
  "builds": [
    {
      "firstItemId": 6653,
      "primaryKeystoneId": 8214,
      "games": 902,
      "pickRate": 0.49,
      "winRate": 0.55,
      "core": {
        "itemPath": { "itemIds": [6653, 3020, 3157], "games": 540, "pickRate": 0.60, "winRate": 0.56 },
        "boots": { "itemIds": [3020], "games": 700, "pickRate": 0.78, "winRate": 0.55 },
        "starterItems": { "itemIds": [1056, 2003], "games": 810, "pickRate": 0.90, "winRate": 0.54 },
        "summonerSpells": { "spell1Id": 4, "spell2Id": 14, "games": 600, "pickRate": 0.66, "winRate": 0.56 },
        "skillOrder": { "sequence": ["Q", "E", "W", "Q", "Q"], "games": 480, "pickRate": 0.53, "winRate": 0.57 },
        "runePage": {
          "primaryStyleId": 8200, "primaryKeystoneId": 8214,
          "primaryPerk1Id": 8226, "primaryPerk2Id": 8210, "primaryPerk3Id": 8237,
          "secondaryStyleId": 8100, "secondaryPerk1Id": 8139, "secondaryPerk2Id": 8135,
          "statOffense": 5008, "statFlex": 5008, "statDefense": 5001,
          "games": 300, "pickRate": 0.33, "winRate": 0.57
        }
      },
      "variations": {
        "boots": [ { "itemIds": [3020], "games": 700, "pickRate": 0.78, "winRate": 0.55 } ],
        "starterItems": [ { "itemIds": [1056, 2003], "games": 810, "pickRate": 0.90, "winRate": 0.54 } ],
        "summonerSpells": [ { "spell1Id": 4, "spell2Id": 14, "games": 600, "pickRate": 0.66, "winRate": 0.56 } ],
        "skillOrder": [ { "sequence": ["Q", "E", "W"], "games": 480, "pickRate": 0.53, "winRate": 0.57 } ]
      },
      "buildTree": [
        {
          "itemId": 3020, "games": 540, "wins": 300, "pickRate": 0.60,
          "children": [ { "itemId": 3157, "games": 280, "wins": 160, "pickRate": 0.52, "children": [] } ]
        }
      ],
      "runePages": [
        {
          "primaryStyleId": 8200, "primaryKeystoneId": 8214,
          "primaryPerk1Id": 8226, "primaryPerk2Id": 8210, "primaryPerk3Id": 8237,
          "secondaryStyleId": 8100, "secondaryPerk1Id": 8139, "secondaryPerk2Id": 8135,
          "statOffense": 5008, "statFlex": 5008, "statDefense": 5001,
          "games": 300, "pickRate": 0.33, "winRate": 0.57
        }
      ]
    }
  ]
}
```

- Chaque onglet (`builds[]`) est clé par `(firstItemId, primaryKeystoneId)`.
- `core` = choix dominant par dimension ; `variations` = top-N par dimension.
- `buildTree` = arbre d'items enraciné sur `firstItemId` (racine implicite).
- `totalGames`/`totalWins` = dénominateurs pour le winrate champion-wide.

### Filtre matchup (`opponentChampionId`)

| Param | Type | Requis | Description |
|-------|------|--------|-------------|
| `opponentChampionId` | int | non | Adversaire de voie. Re-calcule **toutes** les sections build (variations, core, build tree, runes, skill order) sur les seules parties où les deux champions se sont affrontés. |

Exige `position` : le self-join apparie les deux camps dessus, donc un matchup sans voie est un **400**.
Renvoie **404** quand la fenêtre retenue ne contient aucune partie de ce matchup — distinct d'une réponse vide.

La réponse a exactement la même forme que sans le filtre. Deux différences de fond :

- Les données viennent d'un repli **live** de `match_participants` (les agrégats de patterns n'ont pas de
  dimension adversaire), borné aux 2 patchs retenus par la rétention et plafonné à 2 000 parties.
- L'échantillon est petit par nature — mesuré en prod, la paire médiane champion × adversaire × position tient
  **4 parties** sur un patch. Chaque variation porte donc son `games`, et le front l'affiche.

## `GET /champions/{championId}/trend`

Évolution winrate/pickrate/banrate sur les ~5 derniers patchs, pour une position.
Volontairement **cross-patch** (ignore tout filtre de patch).

**Query** — `position` (string, optionnel ; défaut = lane dominante du champion)

**Réponse `200`** — `ChampionTrendReadModel` (toujours `200`, série possiblement vide)

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "points": [
    { "patch": "16.1", "winRate": 0.51, "pickRate": 0.061, "banRate": null, "games": 1500 },
    { "patch": "16.2", "winRate": 0.52, "pickRate": 0.066, "banRate": null, "games": 1620 },
    { "patch": "16.4", "winRate": 0.533, "pickRate": 0.072, "banRate": 0.184, "games": 1840 }
  ]
}
```

- `banRate` : `null` sur tout patch antérieur à la collecte des bans (#920), donc une
  série partiellement nulle est le cas normal au début. Toutes tranches d'elo confondues
  (l'endpoint n'a pas de filtre de rang).

## `GET /champions/{championId}/matchups`

Matchups de lane : chaque adversaire direct rencontré au-dessus d'un plancher de
games, avec games / wins / winrate, calculé en live.

**Query**

| Param      | Type | Requis | Description |
|------------|------|--------|-------------|
| `position` | string | **oui** | Position Riot. Non reconnue → `400`. |
| `patch`    | string | non | Filtre patch. Omis → tous patchs. |
| `opponent` | int (≥1) | non | Restreint à un seul adversaire (plancher = 1 game). |

**Réponse `200`** — `ChampionMatchupsResponse` (liste triée par winRate décroissant)

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "matchups": [
    { "opponentChampionId": 1, "games": 60, "wins": 38, "winRate": 0.633 },
    { "opponentChampionId": 157, "games": 92, "wins": 40, "winRate": 0.435 }
  ]
}
```

`patch` est `null` quand aucun patch n'a été épinglé.

## `GET /champions/{championId}/scaling`

Winrate en fonction de la durée de game, plus un indice de scaling (winrate des
games longues − winrate des games courtes ; positif = scale late).

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel)

**Réponse `200`** — `ChampionScalingResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "buckets": [
    { "bucket": 0, "label": "0-20 min", "games": 220, "winRate": 0.48 },
    { "bucket": 2, "label": "25-30 min", "games": 540, "winRate": 0.53 },
    { "bucket": 4, "label": "35+ min", "games": 180, "winRate": 0.58 }
  ],
  "scalingIndex": 0.10
}
```

`scalingIndex` est `null` s'il n'y a pas assez de buckets qualifiés.

## `GET /champions/{championId}/item-timings`

Heure d'achat moyenne (premier achat) de chaque item, ordonnée du plus précoce
au plus tardif.

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel)

**Réponse `200`** — `ChampionItemTimingsResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "items": [
    { "itemId": 1056, "games": 1800, "avgSeconds": 35.0 },
    { "itemId": 6653, "games": 1500, "avgSeconds": 980.4 },
    { "itemId": 3020, "games": 1400, "avgSeconds": 1120.7 }
  ]
}
```

`avgSeconds` = temps de jeu moyen du premier achat de l'item, en secondes.

## `GET /champions/{championId}/roam`

Propension au roam : nombre moyen de participations à des kills (kills + assists)
hors de la lane par partie, mesuré aux paliers 5/10/15 minutes (cumulatifs).
Un roam est une participation dans une autre lane, la jungle ennemie ou la base
ennemie — la rivière et sa propre jungle ne comptent pas.

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel)

**Réponse `200`** — `ChampionRoamResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "games": 1840,
  "roamKp5": 0.42,
  "roamKp10": 1.15,
  "roamKp15": 2.03
}
```

`roamKp5/10/15` = moyennes cumulatives par partie ; `null` sous le plancher
d'échantillon et pour `JUNGLE` (pas de lane propre).

## `GET /champions/{championId}/powerspikes`

Événements de power spike (items complétés, paliers de niveau 6/11/16) avec leur
magnitude, **scopés à un seul build core**.

**Query** — `position` (**requis**, `400` sinon), `buildFirstItemId` et
`buildKeystoneId` (**requis**, positifs, `400` sinon), `patch` (optionnel),
`eloBracket` (optionnel)

Le couple `buildFirstItemId` / `buildKeystoneId` identifie le build core de la
même façon que la lecture des builds clé ses onglets — un build jamais joué
renvoie une liste vide plutôt qu'un repli sur les autres builds du champion.

**Réponse `200`** — `ChampionPowerspikesResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "events": [
    { "type": "item", "refId": 6653, "avgMinute": 16.3, "spikeMagnitude": 0.18, "games": 1500 },
    { "type": "level", "refId": 6, "avgMinute": 6.8, "spikeMagnitude": 0.09, "games": 1700 }
  ]
}
```

- `events[].type` : `item` (`refId` = item id) ou `level` (`refId` = 6/11/16).
- `events[].spikeMagnitude` : accélération de l'avance sur l'adversaire, en excès
  de la courbure ambiante de la courbe moyenne. La courbe elle-même n'est plus
  renvoyée : elle ne sert plus que de baseline côté serveur.

---

# Truemains — `/truemains`

Public. Profils de joueurs, leaderboard, historique. `429` sous rate-limit.

## `GET /truemains/search`

Lookup name/tag pour la barre de recherche.

**Query**

| Param   | Type   | Requis | Description |
|---------|--------|--------|-------------|
| `q`     | string | non    | Nom partiel, ou Riot ID complet `Name#TAG`. |
| `limit` | int    | non    | Max résultats. Omis/≤0 → défaut du service. |

**Réponse `200`** — `SearchResponse` (toujours `200`, liste possiblement vide)

```json
{
  "results": [
    {
      "identity": { "gameName": "Faker", "tagLine": "KR1", "platformId": "KR", "profileIconId": 6, "summonerLevel": 540 },
      "region": "korea",
      "ranked": { "tier": "CHALLENGER", "division": "I", "leaguePoints": 1245 }
    }
  ]
}
```

`region` ∈ `europe` / `americas` / `korea`. `ranked` est `null` sans snapshot.

## `GET /truemains`

Leaderboard paginé des truemains. Pose un en-tête
`Cache-Control: public, s-maxage=30, stale-while-revalidate=60`.

**Query**

| Param        | Type   | Requis | Défaut | Description |
|--------------|--------|--------|--------|-------------|
| `page`       | int    | non    | 1      | Page 1-based. |
| `pageSize`   | int    | non    | défaut | 0/omis → taille par défaut. |
| `region`     | string | non    | toutes | `europe`/`americas`/`korea`. |
| `position`   | string | non    | toutes | Filtre position. |
| `championId` | int    | non    | tous   | Filtre champion principal. |
| `otpOnly`    | bool   | non    | false  | Restreint aux one-tricks. |
| `sort`       | string | non    | `rank` | `dedication` classe par score de dédication ; toute autre valeur retombe sur le classement par rang. |

**Réponse `200`** — `LeaderboardResponse`

```json
{
  "rows": [
    {
      "rank": 1,
      "identity": { "gameName": "Faker", "tagLine": "KR1", "platformId": "KR", "profileIconId": 6, "summonerLevel": 540 },
      "region": "korea",
      "ranked": { "tier": "CHALLENGER", "division": "I", "leaguePoints": 1245, "score": 28245 },
      "stats": { "games": 412, "wins": 240, "losses": 172, "winRate": 0.583, "kda": 3.4 },
      "topChampions": [
        { "championId": 103, "games": 120, "playRate": 0.29, "primaryKeystoneId": 8214, "secondaryStyleId": 8100, "firstItemId": 6653 }
      ],
      "dedication": {
        "score": 78.4, "championId": 103,
        "commitment": 0.193, "span": 1, "volume": 0.912, "recency": 0.968,
        "playRate": 0.29, "careerGames": 120, "patchSpan": 7, "daysSinceLastGame": 1
      }
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 480
}
```

- `ranked.score` : clé de tri SQL exposée. `ranked` peut être `null` (trié en dernier).
- `stats.wins`/`losses`/`winRate`/`kda` peuvent être `null` si aucune game attribuée.
- `dedication` : score de dédication (0..100) du champion signature de la ligne,
  avec ses quatre composantes et leurs entrées brutes. `null` si l'analyse des
  mains n'a pas encore tourné. `championId` est le **seul** filtre qui déplace le
  score sur un autre champion ; `position`, `otpOnly` et le plancher
  `MinRankedGames` ne font que restreindre la population — un toplaner qui main
  aussi un champion mid garde donc son score toplane sous `?position=MIDDLE`.
  Corollaire : pour un même compte, le score et son `championId` sont identiques
  quel que soit le `sort`, et identiques à ceux du profil.
  Formule et calibrage : [`docs/dedication-score.md`](dedication-score.md).

## `GET /truemains/{nameTag}/profile`

Profil d'un joueur. `nameTag` est le Riot ID (`Name#TAG`, URL-encodé).

**Réponse `200`** — `ProfileReadModel` · **`404`** si compte inconnu.

```json
{
  "identity": { "gameName": "Faker", "tagLine": "KR1", "platformId": "KR", "profileIconId": 6, "summonerLevel": 540 },
  "ranked": { "tier": "CHALLENGER", "division": "I", "leaguePoints": 1245, "wins": 240, "losses": 172, "winRate": 0.583 },
  "mains": [
    { "championId": 103, "games": 120, "playRate": 0.29, "primaryPosition": "MIDDLE", "isOtp": false }
  ],
  "dedication": {
    "score": 78.4, "championId": 103,
    "commitment": 0.193, "span": 1, "volume": 0.912, "recency": 0.968,
    "playRate": 0.29, "careerGames": 120, "patchSpan": 7, "daysSinceLastGame": 1
  },
  "positions": [
    { "position": "MIDDLE", "games": 300, "rate": 0.72 },
    { "position": "TOP", "games": 116, "rate": 0.28 }
  ]
}
```

`dedication` porte sur le champion signature du joueur (son main le plus joué) ;
`null` si aucun champion n'est encore classé comme main. Voir
[`docs/dedication-score.md`](dedication-score.md).

## `GET /truemains/{nameTag}/champions/{championId}`

Page champion **scopée au joueur** : même contrat que `GET /champions/{id}`, mais
agrégé uniquement sur les games de ce joueur.

**Query** — `patch` (optionnel), `position` (optionnel)

**Réponse `200`** — `ChampionResponse` (même forme qu'au-dessus) ·
**`404`** si compte inconnu ou trop peu de games sur le champion.

## `GET /truemains/{nameTag}/champions/{championId}/matchups`

Matchups de lane **scopés au joueur** : même contrat que
`GET /champions/{id}/matchups`.

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel), `opponent` (int ≥1, optionnel)

**Réponse `200`** — `ChampionMatchupsResponse` · **`404`** si compte inconnu.
Un joueur connu sans adversaire au-dessus du plancher → `200` avec liste vide.

## `GET /truemains/{nameTag}/champions/{championId}/performance`

Score de performance **scopé au joueur** : moyenne du score par match sur ses
parties récentes sur ce champion, avec le détail par composante. Voir
[`docs/performance-score.md`](performance-score.md).

**Query** — `patch` (optionnel), `position` (optionnel, `400` si invalide)

**Réponse `200`** — `PlayerChampionPerformanceResponse` · **`404`** si `nameTag`
malformé ou compte inconnu. Un joueur connu sous le plancher d'échantillon → `200`
avec `games` et toutes les moyennes à `null`.

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "15.12",
  "games": 14,
  "minGames": 5,
  "window": 20,
  "averageScore": 71.4,
  "bestScore": 88,
  "worstScore": 41,
  "topOfTeamRate": 0.36,
  "components": [
    { "kind": "Combat", "weight": 20, "value": 0.74, "games": 14 },
    { "kind": "Laning", "weight": 10, "value": 0.58, "games": 12 },
    { "kind": "MidGame", "weight": 6, "value": 0.61, "games": 9 },
    { "kind": "Roam", "weight": 6, "value": 0.44, "games": 11 }
  ]
}
```

- `window` : nombre de parties les plus récentes prises en compte (métrique de
  forme, pas de carrière).
- `games` d'une composante ≤ `games` global : une partie sans couverture timeline
  est **exclue** de la moyenne de la composante au lieu d'y compter zéro.
- `weight` est le poids nominal du rôle (moyenné si le joueur a changé de lane sur
  l'échantillon) ; `0` = le rôle ne note pas cette composante.

## `GET /truemains/{nameTag}/rank-history`

Historique de rang (snapshots append-on-change).

**Query** — `days` (int, optionnel ; 0/omis = tout l'historique)

**Réponse `200`** — `RankHistoryReadModel` · **`404`** si compte inconnu.

```json
{
  "entries": [
    { "capturedAtUtc": "2026-05-01T12:00:00Z", "tier": "GRANDMASTER", "division": "I", "leaguePoints": 420 },
    { "capturedAtUtc": "2026-06-10T09:30:00Z", "tier": "CHALLENGER", "division": "I", "leaguePoints": 1100 }
  ]
}
```

## `GET /truemains/{nameTag}/activity`

Grille d'activité affichée sous la courbe de LP du profil (#927) : les parties du
joueur repliées **par partie**, **par jour UTC** et **par semaine ISO**, plus
l'historique **par patch** de son champion signature.

**Query** — aucune. Les quatre granularités arrivent dans la même réponse : trois
d'entre elles sont des replis des *mêmes* lignes `match_participants`, donc les
calculer d'un seul instantané est à la fois moins cher que quatre allers-retours et
la seule façon de garantir que basculer le sélecteur ne montre pas deux réponses
différentes pour le même après-midi.

**Réponse `200`** — `TruemainActivityReadModel` · **`404`** si le nameTag est
malformé ou le compte inconnu.

```json
{
  "day": {
    "mode": "day",
    "source": "matches",
    "scope": "allChampions",
    "championId": null,
    "retentionBounded": true,
    "coverageFromUtc": "2026-07-03T00:00:00Z",
    "coverageToUtc": "2026-07-29T00:00:00Z",
    "buckets": [
      { "key": "2026-07-27", "startUtc": "2026-07-27T00:00:00Z", "games": 3, "wins": 2, "winRate": 0.667, "championId": null },
      { "key": "2026-07-28", "startUtc": "2026-07-28T00:00:00Z", "games": 0, "wins": 0, "winRate": null, "championId": null }
    ],
    "games": 42, "wins": 25, "winRate": 0.595
  },
  "game": { "mode": "game", "…": "une cellule par partie, championId renseigné" },
  "week": { "mode": "week", "…": "une cellule par lundi 00:00 UTC" },
  "patch": {
    "mode": "patch",
    "source": "aggregates",
    "scope": "champion",
    "championId": 157,
    "retentionBounded": false,
    "coverageFromUtc": null,
    "coverageToUtc": null,
    "buckets": [
      { "key": "15.13", "startUtc": null, "games": 26, "wins": 15, "winRate": 0.577, "championId": null }
    ],
    "games": 180, "wins": 98, "winRate": 0.544
  }
}
```

**Les quatre séries ne décrivent pas la même population, et la réponse le dit.**
C'est l'asymétrie de rétention : `match_participants` est purgé au-delà de
`MatchDataRetention:RetainedPatchCount` patches (~2) mais porte la date d'une
partie, alors que `champion_aggregate_scopes` est gelé pour toujours (#466) mais
n'a qu'un grain (compte, champion, patch).

- `source` / `scope` / `retentionBounded` : `game`/`day`/`week` lisent les lignes de
  match vivantes, tous champions confondus, et s'arrêtent à la fenêtre de
  rétention. `patch` lit l'agrégat gelé, **uniquement sur le champion signature**
  (celui de la carte dédication — même sélection, mêmes lignes sommées).
- `coverageFromUtc` / `coverageToUtc` : la période dont la série peut réellement
  parler. Les séries calendaires **n'émettent pas** de cellule avant la plus vieille
  partie encore stockée : une période effacée n'est pas une période sans jeu, donc
  la dessiner vide serait un « tu n'as pas joué » fabriqué. `null` sur la série
  `patch`, dont l'étendue est une liste de patches, pas un intervalle de dates.
- `winRate` est `null` — jamais `0` — quand `games` vaut 0. C'est la distinction
  filaire entre « joué et tout perdu » et « pas joué » ; les deux ne doivent pas se
  rendre pareil.
- Les jours et les semaines sont **UTC** (lundi 00:00 UTC pour les semaines), comme
  le reste du pipeline (#907) : une partie de fin de soirée peut donc tomber sur la
  cellule du lendemain pour un joueur loin d'UTC.
- Invariants vérifiables à l'œil sur la page : `patch.games ==
  dedication.careerGames` et `patch.buckets.length == dedication.patchSpan`.
- `championId` de cellule n'est renseigné que sur la série `game` (une cellule =
  une partie).

## `GET /truemains/{nameTag}/matches`

Historique de matchs paginé.

**Query**

| Param        | Type | Requis | Défaut | Description |
|--------------|------|--------|--------|-------------|
| `page`       | int  | non    | 1      | Page 1-based. |
| `pageSize`   | int  | non    | défaut | 0/omis → défaut. |
| `position`   | string | non  | toutes | Filtre position. |
| `championId` | int  | non    | tous   | Filtre champion. |

**Réponse `200`** — `MatchSummariesResponse` · **`404`** si compte inconnu.

```json
{
  "matches": [
    {
      "matchId": "KR_7654321",
      "queueId": 420,
      "gameMode": "CLASSIC",
      "gameStartTimeUtc": "2026-06-25T18:42:00Z",
      "gameDurationSeconds": 1832,
      "self": {
        "championId": 103, "championLevel": 16,
        "summoner1Id": 4, "summoner2Id": 14,
        "primaryStyleId": 8200, "subStyleId": 8100, "keystoneId": 8214,
        "kills": 8, "deaths": 3, "assists": 11, "cs": 245,
        "killParticipation": 0.62,
        "items": [6653, 3020, 3157, 3089, 3135, 0],
        "trinketItemId": 3340,
        "teamId": 100, "win": true,
        "lpDelta": null,
        "performanceScore": 78, "placement": 2,
        "isMvp": true, "isAce": false
      },
      "participants": [
        { "championId": 103, "teamId": 100, "gameName": "Faker", "tagLine": "KR1" },
        { "championId": 157, "teamId": 200, "gameName": null, "tagLine": null }
      ]
    }
  ],
  "page": 1,
  "pageSize": 20,
  "total": 412
}
```

- `self.items` : 6 slots d'inventaire (0 = vide) ; trinket séparé dans `trinketItemId`.
- `participants` : les 10 joueurs (team 100 puis 200). `gameName`/`tagLine` `null`
  si le participant n'est pas un compte suivi.
- `lpDelta` est toujours `null` dans cette itération.
- `performanceScore` / `placement` / `isMvp` / `isAce` sortent du **même** scoreur
  que le détail du match (voir [`docs/performance-score.md`](performance-score.md)),
  donc la ligne repliée et le panneau déplié ne peuvent pas se contredire.

## `GET /truemains/{nameTag}/matches/{matchId}`

Détail complet d'un match auquel le joueur a participé : les 10 participants avec
leur build order, skill order, page de runes et les stats de lane dérivées de la
timeline. `nameTag` scope la route (le compte doit avoir joué ce match) mais la
réponse couvre tous les participants.

**Réponse `200`** — `MatchDetailReadModel` · **`404`** si `nameTag` malformé,
compte inconnu, ou match non joué par ce compte.

```json
{
  "matchId": "KR_7654321",
  "queueId": 420,
  "gameMode": "CLASSIC",
  "gameStartTimeUtc": "2026-06-25T18:42:00Z",
  "gameDurationSeconds": 1832,
  "gameVersion": "16.4.521",
  "participants": [
    {
      "participantId": 1,
      "championId": 103,
      "champLevel": 16,
      "summonerName": "Faker",
      "gameName": "Faker",
      "tagLine": "KR1",
      "teamId": 100,
      "teamPosition": "MIDDLE",
      "win": true,
      "kills": 8, "deaths": 3, "assists": 11,
      "items": [6653, 3020, 3157, 3089, 3135, 0, 0],
      "trinketItemId": 3340,
      "summoner1Id": 4, "summoner2Id": 14,
      "primaryStyleId": 8200, "subStyleId": 8100, "keystoneId": 8214,
      "totalDamageDealtToChampions": 28400,
      "visionScore": 32,
      "goldEarned": 14200,
      "cs": 245,
      "rank": { "tier": "CHALLENGER", "division": "I", "leaguePoints": 1245 },
      "killParticipation": 0.62,
      "csPerMin": 8.0,
      "damagePerMin": 930.1,
      "goldPerMin": 465.0,
      "visionPerMin": 1.05,
      "performanceScore": 86,
      "placement": 1,
      "isMvp": true,
      "isAce": false,
      "laning15": { "csDiff": 12, "goldDiff": 480, "xpDiff": 210 },
      "firstToLevelTwo": true,
      "runes": [
        { "styleId": 8200, "selectionIndex": 0, "perkId": 8214 },
        { "styleId": 8100, "selectionIndex": 4, "perkId": 8135 }
      ],
      "statPerkOffense": 5008,
      "statPerkFlex": 5008,
      "statPerkDefense": 5001,
      "itemEvents": [
        { "timestampMs": 21000, "eventType": "ITEM_PURCHASED", "itemId": 1056, "beforeId": null, "afterId": null }
      ],
      "skillEvents": [
        { "timestampMs": 90000, "skillSlot": 2 }
      ]
    }
  ]
}
```

- `participants` : les 10 joueurs. `gameName`/`tagLine` `null` si non suivi ;
  `items` = 7 slots d'inventaire (0 = vide), trinket séparé dans `trinketItemId`.
- Champs dérivés côté serveur : `killParticipation`, `*PerMin`, `laning15` (diffs
  @15 vs l'adversaire de lane, `null` si un snapshot @15 manque) et `firstToLevelTwo`
  (`null` sans adversaire de lane ou timeline de skills).
- `rank` = tier approximatif au moment du match (snapshot le plus proche), `null` sinon.
- `itemEvents.eventType` : `ITEM_PURCHASED` / `ITEM_SOLD` / `ITEM_DESTROYED` /
  `ITEM_UNDO` (`beforeId`/`afterId` renseignés sur un undo).
  `skillEvents.skillSlot` : 1=Q, 2=W, 3=E, 4=R.

### Score de performance (`performanceScore` / `placement` / `isMvp` / `isAce`)

Métrique **dérivée** (aucun stockage dédié), calculée à la lecture par
`Core.Lol.Performance.PerformanceScore` — fonction pure et déterministe : mêmes
entrées, même score. Sept composantes sont normalisées sur `0..1` puis moyennées
avec des poids qui dépendent du rôle (`teamPosition`), le résultat étant remis à
l'échelle `0..100` :

| Composante | Normalisation |
| --- | --- |
| Combat | `(kills + assists) / max(1, deaths)`, linéaire jusqu'à 6.0 KDA = plein |
| Kill participation | `(kills + assists) / teamKills`, borné à 1 |
| Part de dégâts | part des dégâts aux champions de l'équipe, bande 5 % → 35 % |
| Part d'or | part de l'or de l'équipe, bande 10 % → 30 % |
| Farm | CS/min vs une référence par rôle (2.0 support … 9.5 bot) |
| Vision | vision score/min vs une référence par rôle (0.8 bot … 2.4 support) |
| Lane | avances @15 mixées or 50 % / cs 25 % / xp 25 %, centrées (lane égale = 0.5), saturées à ±1500 or, ±30 cs, ±1500 xp |

Poids par rôle (somme = 100 ; `teamPosition` vide ou inconnu → profil neutre) :

| Rôle | Combat | KP | Dégâts | Or | Farm | Vision | Lane |
| --- | --- | --- | --- | --- | --- | --- | --- |
| TOP | 22 | 16 | 18 | 8 | 16 | 6 | 14 |
| JUNGLE | 20 | 20 | 16 | 8 | 16 | 8 | 12 |
| MIDDLE | 22 | 16 | 20 | 8 | 16 | 6 | 12 |
| BOTTOM | 22 | 14 | 22 | 8 | 18 | 4 | 12 |
| UTILITY | 22 | 22 | 8 | 4 | 6 | 26 | 12 |
| neutre | 22 | 18 | 18 | 8 | 14 | 8 | 12 |

Une composante dont l'entrée manque (pas de snapshot @15, `teamKills` à 0, partie
de durée nulle…) est **retirée** et son poids redistribué sur les autres — jamais
comptée comme un zéro, ce qui pénaliserait un joueur pour un trou dans nos données.

`placement` = rang 1..10 du score dans le match (1 = meilleur). Les égalités se
départagent sur takedowns, puis morts, puis `participantId`, donc le classement est
toujours strict et stable entre deux requêtes identiques. `isMvp` = meilleur score
du camp gagnant, `isAce` = meilleur score du camp perdant.

Hors modèle, faute de données stockées : participation aux objectifs (dragon /
baron / tourelles), dégâts subis, soins/boucliers, wards posées. Pas de bonus de
victoire non plus : le score note l'individu, et les vainqueurs remontent déjà
naturellement via le KDA, le farm et les avances de lane.

---

# Ops — `/ops`

**Protégé.** Toutes les requêtes exigent l'en-tête `X-Ops-Key: <OPS_API_KEY>`.
Sans clé valide → `401 Unauthorized`. Endpoints d'administration / observabilité.

## `GET /ops/pipeline-health`

Santé du pipeline d'ingestion.

**Réponse `200`** — `PipelineHealthReadModel`

```json
{
  "processes": [
    { "processName": "Discovery", "status": "Success", "lastStartedAtUtc": "2026-06-26T10:00:00Z", "lastFinishedAtUtc": "2026-06-26T10:02:00Z", "durationMs": 120000, "error": null }
  ],
  "rawData": {
    "queueId": 420,
    "rawMatchCount": 1500000,
    "rawParticipantCount": 15000000,
    "platforms": [
      { "platformId": "EUW1", "latestMatchStartAtUtc": "2026-06-26T09:50:00Z", "latestPatchVersion": "16.4" }
    ]
  },
  "gaps": { "matchIngestionToMainAnalysisMinutes": 12.5, "championDataLagMinutes": 30.0 }
}
```

## `GET /ops/stats/overview`

Compteurs globaux du corpus.

**Réponse `200`** — `OverviewReadModel`

```json
{
  "trackedAccounts": 12000,
  "totalMatches": 1500000,
  "totalParticipants": 15000000,
  "candidatesByStatus": { "New": 200, "Scored": 80, "Queued": 15, "Processing": 3, "Validated": 9000, "Rejected": 1200 },
  "totalMains": 9000,
  "totalOtps": 1300,
  "distinctChampionsWithGames": 168,
  "distinctChampionsWithMains": 165,
  "matchesLast7Days": 42000,
  "matchesLast30Days": 180000
}
```

## `GET /ops/stats/champions`

Stats par champion sur le corpus (filtrable).

**Query** — `region` (PlatformId), `patch`, `position`, `queue` (int) — tous optionnels.

**Réponse `200`** — `ChampionStatRow[]`

```json
[
  { "championId": 103, "games": 18400, "mains": 47, "otps": 6, "extendedSamples": 120 }
]
```

> Les compteurs mains/otps/extendedSamples sont scopés par `region` uniquement
> (patch/position/queue ne s'appliquent qu'à `games`).

## `GET /ops/stats/matches-over-time`

Histogramme du nombre de matchs dans le temps, par date de game.

**Query**

| Param         | Type   | Requis | Description |
|---------------|--------|--------|-------------|
| `granularity` | string | **oui** | `week` / `month` / `year` / `patch`. Manquant/invalide → `400`. |
| `region`      | string | non    | Filtre PlatformId. |

**Réponse `200`** — `MatchTimeBucket[]` (chronologique)

```json
[
  { "bucket": "2026-06-01T00:00:00Z", "matches": 52000 },
  { "bucket": "2026-06-08T00:00:00Z", "matches": 48000 }
]
```

`bucket` = timestamp ISO du début de période (week/month/year), ou `MAJEUR.MINEUR`
(`16.4`) pour `patch`.

## `GET /ops/db/tables`

Empreinte de stockage des tables Postgres.

**Réponse `200`** — `TableStatRow[]`

```json
[
  { "tableName": "match_participants", "rowEstimate": 15000000, "totalBytes": 8589934592, "tableBytes": 5368709120, "indexBytes": 3221225472 }
]
```

## `GET /ops/db/history`

Croissance du stockage + prévision de saturation disque (#925). Lit uniquement les
snapshots quotidiens (collection Mongo `db_table_size_snapshots`) — aucun scan
`pg_catalog` à la volée, contrairement à `db/tables`.

**Query**

| Param        | Type | Requis | Défaut                        | Description |
|--------------|------|--------|-------------------------------|-------------|
| `windowDays` | int  | non    | `StorageHistory:DefaultWindowDays` (90) | Fenêtre d'historique en jours. |

**Réponse `200`** — `DbStorageHistoryReadModel` (toujours `200`, tout vide tant que le
process de snapshot n'a pas tourné)

```json
{
  "daily": [
    { "dateUtc": "2026-07-27T00:00:00Z", "databaseBytes": 41231686144, "totalBytes": 39000000000, "rowEstimate": 21000000 }
  ],
  "tables": [
    {
      "tableName": "match_participants",
      "points": [{ "dateUtc": "2026-07-27T00:00:00Z", "totalBytes": 8589934592, "rowEstimate": 15000000 }],
      "currentBytes": 8589934592,
      "bytesPerDay": 130000000,
      "rowsPerDay": 240000,
      "growthRate": 0.12
    }
  ],
  "forecast": {
    "bytesPerDay": 310000000,
    "diskCapacityBytes": 107374182400,
    "crossings": [
      { "percent": 80, "thresholdBytes": 85899345920, "projectedAtUtc": "2026-11-14T00:00:00Z" },
      { "percent": 100, "thresholdBytes": 107374182400, "projectedAtUtc": null }
    ]
  }
}
```

- `databaseBytes` : `pg_database_size` **mesuré** — c'est ce qui remplit le volume
  (catalogues compris), et c'est ce que la prévision extrapole. `totalBytes` n'est que
  la somme des tables du schéma `public`, donc toujours plus petit.
- `rowEstimate` : somme des estimations du planner, indicateur de tendance et non un
  compte exact.
- `tables` : uniquement les plus grosses (`StorageHistory:TopTables`, 10 par défaut) ;
  les autres restent comptées dans `daily`.
- `growthRate` : `null` si la table était vide en début de fenêtre (croissance non
  définie plutôt qu'infinie).
- `forecast` : **`null`** s'il y a moins de 3 jours d'historique, si le stockage est
  stable ou décroissant, ou si `StorageHistory:DiskCapacityBytes` n'est pas configuré.
  Aucune valeur de remplacement n'est inventée.
- `projectedAtUtc` : `null` = échéance à plus d'un siècle **dans un sens ou dans
  l'autre** (aucune date exploitable à ce rythme) ; une date passée = seuil déjà franchi.

## `GET /ops/process-runs`

Une page de runs de process (récents d'abord) + rollup par process.

**Query**

| Param         | Type     | Requis | Description |
|---------------|----------|--------|-------------|
| `processName` | string   | non | Restreint à un process. |
| `status`      | string   | non | Nom de `ProcessRunStatus` (insensible casse). |
| `since`       | datetime | non | Borne basse sur `StartedAtUtc` (+ fenêtre du rollup). |
| `limit`       | int      | non | Taille de page legacy (alias de `pageSize`). |
| `page`        | int      | non | 1-based (clamp ≥ 1). |
| `pageSize`    | int      | non | Clamp [1, 500], défaut 100. |

**Réponse `200`** — `ProcessRunsReadModel`

```json
{
  "runs": [
    {
      "id": "0b1c2d3e-4f56-7890-abcd-ef0123456789",
      "processName": "MainAnalysis",
      "startedAtUtc": "2026-06-26T10:00:00Z",
      "finishedAtUtc": "2026-06-26T10:05:00Z",
      "durationMs": 300000,
      "status": "Success",
      "error": null,
      "host": "ingestor-1",
      "lastHeartbeatAtUtc": null,
      "summary": { "accountsProcessed": 320, "matchesIngested": 4100 }
    }
  ],
  "rollup": [
    { "processName": "MainAnalysis", "lastStatus": "Success", "lastRunAtUtc": "2026-06-26T10:00:00Z", "lastSuccessAtUtc": "2026-06-26T10:00:00Z", "failureCountInWindow": 0, "runCountInWindow": 48, "failureRateInWindow": 0.0 }
  ],
  "total": 1240,
  "page": 1,
  "pageSize": 100
}
```

- `status` peut être `Success`/`Failed`/`Running`/`Abandoned` (un `Running` à
  heartbeat périmé est rapporté `Abandoned`).
- `summary` est le payload JSONB du run, verbatim (ou `null`).

## `GET /ops/process-iterations`

Itérations récentes du pipeline (une passe complète = une itération), récentes
d'abord, chacune portant ses runs ordonnés.

**Query**

| Param          | Type | Requis | Description |
|----------------|------|--------|-------------|
| `page`         | int  | non | 1-based (clamp ≥ 1). |
| `pageSize`     | int  | non | Clamp [1, 50], défaut 10. |
| `finishedOnly` | bool | non | Exclut l'itération en cours. Défaut `false`. |

**Réponse `200`** — `ProcessIterationsReadModel`

```json
{
  "iterations": [
    {
      "iterationId": "1a2b3c4d-5e6f-7081-92a3-b4c5d6e7f809",
      "startedAtUtc": "2026-06-26T10:00:00Z",
      "lastActivityAtUtc": "2026-06-26T10:12:00Z",
      "isRunning": false,
      "runs": [
        { "id": "…", "processName": "Discovery", "startedAtUtc": "2026-06-26T10:00:00Z", "finishedAtUtc": "2026-06-26T10:02:00Z", "durationMs": 120000, "status": "Success", "error": null, "host": "ingestor-1", "lastHeartbeatAtUtc": null, "summary": null }
      ]
    }
  ],
  "total": 96,
  "page": 1,
  "pageSize": 10
}
```

`runs[]` ont la même forme que `ProcessRunReadModel` ci-dessus.

## `GET /ops/logs`

Page de logs applicatifs persistés (Mongo), récents d'abord.

**Query**

| Param       | Type     | Requis | Description |
|-------------|----------|--------|-------------|
| `level`     | string   | non | Niveau (`Warning`, `Error`…). |
| `category`  | string   | non | Catégorie de logger. |
| `since`     | datetime | non | Borne basse temporelle. |
| `search`    | string   | non | Recherche texte. |
| `eventType` | string   | non | Nom d'ops-event (ex. `CandidateValidated`). |
| `page`      | int      | non | 1-based. |
| `pageSize`  | int      | non | Taille de page. |

**Réponse `200`** — `LogsReadModel`

```json
{
  "entries": [
    {
      "id": "665fd2a1c3b4e5f6a7b8c9d0",
      "timestampUtc": "2026-06-26T10:01:23Z",
      "level": "Warning",
      "category": "TrueMain.Ingestor.RiotClient",
      "message": "Rate limit approached on match-v5",
      "exception": null,
      "processName": "Ingestor",
      "host": "ingestor-1",
      "eventType": null
    }
  ],
  "total": 5400,
  "page": 1,
  "pageSize": 50,
  "eventTypes": ["CandidateValidated", "SeedRequested", "MatchIngested"]
}
```

`eventTypes` = catalogue statique des ops-events (pour peupler le filtre).

## `GET /ops/crashes`

Page de crashs de process enregistrés (Mongo), récents d'abord. Chaque entrée
porte le rapport complet (chaîne d'exceptions, snapshot environnement + mémoire/GC,
et les dernières lignes de log avant le crash) — le panneau Crashes n'a donc pas
besoin d'appel de détail séparé. `sources` et `processes` (catalogues statiques)
accompagnent chaque réponse pour peupler les filtres.

**Query**

| Param      | Type     | Requis | Description |
|------------|----------|--------|-------------|
| `since`    | datetime | non | Borne basse sur l'instant du crash. |
| `process`  | string   | non | `Api` / `Ingestor` (filtre exact). |
| `source`   | string   | non | Nom de `CrashSource` (insensible à la casse). |
| `search`   | string   | non | Recherche message / stack-trace. |
| `page`     | int      | non | 1-based. |
| `pageSize` | int      | non | Taille de page. |

**Réponse `200`** — `CrashesReadModel`

```json
{
  "entries": [
    {
      "id": "665fd2a1c3b4e5f6a7b8c9d0",
      "timestampUtc": "2026-06-26T10:01:23Z",
      "processName": "Ingestor",
      "source": "AppDomainUnhandled",
      "exceptionType": "System.OutOfMemoryException",
      "message": "Exception of type 'System.OutOfMemoryException' was thrown.",
      "stackTrace": "at TrueMain.Ingestor.ChampionPatternAggregation...",
      "innerExceptions": [
        { "type": "System.InvalidOperationException", "message": "Sequence contains no elements", "stackTrace": "at ..." }
      ],
      "host": "ingestor-1",
      "osDescription": "Linux 6.1.0 x64",
      "uptimeSeconds": 3600.5,
      "runtimeVersion": "10.0.9",
      "appVersion": "1.6.0",
      "workingSetBytes": 6100000000,
      "totalManagedMemoryBytes": 5800000000,
      "gen0Collections": 1200,
      "gen1Collections": 300,
      "gen2Collections": 40,
      "exitCode": null,
      "recentLogTail": [
        { "timestampUtc": "2026-06-26T10:01:20Z", "level": "Warning", "category": "TrueMain.Ingestor.PatternAgg", "message": "Heap pressure high", "exception": null }
      ]
    }
  ],
  "total": 12,
  "page": 1,
  "pageSize": 50,
  "sources": ["AppDomainUnhandled", "TaskSchedulerUnobserved", "HostRun", "UncleanShutdown"],
  "processes": ["Api", "Ingestor"]
}
```

Pour un `UncleanShutdown`, les champs d'exception sont `null` et les champs mémoire
portent le dernier snapshot connu du run mort (le signal OOM).

## `GET /ops/riot-usage`

Métriques d'usage de la Riot API sur une fenêtre relative.

**Query**

| Param      | Type   | Requis | Description |
|------------|--------|--------|-------------|
| `window`   | string | non | `1h` / `24h` (défaut) / `7d`. |
| `endpoint` | string | non | Clé d'endpoint exacte (ex. `match-v5.match`). |

**Réponse `200`** — `RiotApiUsageReadModel`

```json
{
  "window": "24h",
  "sinceUtc": "2026-06-25T10:00:00Z",
  "generatedAtUtc": "2026-06-26T10:00:00Z",
  "totalCalls": 480000,
  "totalErrors": 1200,
  "errorRate": 0.0025,
  "avgLatencyMs": 142.5,
  "endpoints": [
    { "endpoint": "match-v5.match", "calls": 300000, "successes": 299100, "errors": 900, "avgLatencyMs": 150.2, "lastCalledAtUtc": "2026-06-26T09:59:50Z" }
  ],
  "statusCodes": [ { "statusCode": 200, "count": 478800 }, { "statusCode": 429, "count": 800 }, { "statusCode": 0, "count": 50 } ],
  "timeSeries": [ { "bucketUtc": "2026-06-26T09:00:00Z", "calls": 20000, "errors": 40 } ],
  "rateLimit": {
    "observedAtUtc": "2026-06-26T09:59:50Z",
    "appRateLimit": "20:1,100:120",
    "appRateLimitCount": "3:1,57:120",
    "methodRateLimit": "2000:10",
    "methodRateLimitCount": "150:10",
    "retryAfterSeconds": null,
    "rateLimitType": null
  }
}
```

`statusCodes[].statusCode == 0` = faute transport (pas de réponse).
`rateLimit` est `null` si aucun en-tête observé dans la fenêtre.

## `GET /ops/data-quality/detectors`

Détecteurs d'anomalies automatiques (#924) : une carte par détecteur, avec son verdict, son
chiffre-titre, les lignes du détail et les seuils configurés (`DataQualityDetectors:*`) contre
lesquels il a jugé.

Pas de paramètre : les seuils sont de la configuration serveur, pas des query params.

`status` vaut `green`, `amber`, `red` ou `unknown`. **`unknown` ne signifie jamais « mesuré et
correct »** : c'est « je n'ai pas pu mesurer », et il l'emporte sur `green` dans l'agrégation
(sans masquer un `red`). Un signal volontairement indisponible (ordre canonique non exprimable
en SQL, tendance sans fenêtre précédente, patch de bord non comparable) apparaît en ligne mais
ne vote pas.

**Réponse `200`** — `DataQualityDetectorsReadModel`

```json
{
  "detectors": [
    {
      "key": "duplicateDimensionRows",
      "title": "Duplicate dimension rows",
      "status": "red",
      "count": 2,
      "countLabel": "canonical-key groups holding more than one row",
      "headline": "2 canonical-key group(s) hold more than one row — those games are split across rows, the #911 failure.",
      "unknownReason": null,
      "sourceNote": "Groups each champion_dim_* table on the same canonical key the ingestor's repair merges on…",
      "rows": [
        {
          "label": "champion_dim_rune_pages",
          "status": "red",
          "value": 1,
          "valueLabel": "1 duplicate group(s)",
          "note": "The two secondary perks are a set, not a sequence (#911). 1 row(s) stored outside canonical order."
        }
      ],
      "thresholds": [
        { "label": "duplicate groups", "amber": 1, "red": 1, "unit": "count" }
      ],
      "hasDrillDownEndpoint": false
    }
  ],
  "evaluatedAtUtc": "2026-07-30T18:00:00Z"
}
```

## `GET /ops/data-quality/aggregate-freshness`

Fraîcheur des agrégats par champion sur les patchs les plus récents, le plus périmé d'abord.
Endpoint séparé et **à la demande** : c'est la seule mesure qui exige un scan groupé de
`champion_aggregate_scopes`, donc elle ne s'exécute pas au chargement du panneau.

**Réponse `200`** — `AggregateFreshnessReadModel`

```json
{
  "patches": ["16.15", "16.14"],
  "champions": [
    {
      "championId": 266, "patch": "16.15",
      "lastAggregatedAtUtc": "2026-07-27T09:00:00Z",
      "ageHours": 75.2, "scopeRows": 12, "status": "red"
    }
  ],
  "championCount": 168,
  "staleChampionCount": 3,
  "staleAfterHours": 6,
  "evaluatedAtUtc": "2026-07-30T18:00:00Z"
}
```

## `GET /ops/data-quality/incomplete-matches`

Matchs signalés par les checks de qualité, groupés par type d'anomalie.

**Query**

| Param         | Type | Requis | Description |
|---------------|------|--------|-------------|
| `issue`       | string | non | Un check : `missingTimeline`, `wrongParticipantCount`, `missingTeamPosition`, `zeroDuration`, `duplicateChampion`. |
| `queue`       | int  | non | Filtre queue (ex. 420). |
| `minAgeHours` | int  | non | Âge minimum des matchs. |
| `page`        | int  | non | 1-based. |
| `pageSize`    | int  | non | Clamp [1, 100], défaut 25. |

**Réponse `200`** — `IncompleteMatchesReadModel`

```json
{
  "groups": [
    {
      "issueType": "missingTimeline",
      "count": 320,
      "matches": [
        {
          "matchId": "EUW1_6543210", "platformId": "EUW1", "queueId": 420,
          "gameStartTimeUtc": "2026-06-20T14:00:00Z", "gameDurationSeconds": 1700,
          "timelineIngested": false, "participantCount": 10, "expectedParticipantCount": 10,
          "issues": ["missingTimeline"]
        }
      ]
    }
  ],
  "total": 512,
  "page": 1,
  "pageSize": 25,
  "staleTimelineThresholdHours": 24
}
```

## `GET /ops/data-quality/match/{id}`

Détail qualité d'un match : les deux équipes par position, anomalies identifiées.

**Path** — `id` (string, match id)

**Réponse `200`** — `MatchDataQualityDetailReadModel` · **`404`** si match inconnu.

```json
{
  "matchId": "EUW1_6543210",
  "platformId": "EUW1",
  "queueId": 420,
  "gameMode": "CLASSIC",
  "gameStartTimeUtc": "2026-06-20T14:00:00Z",
  "gameDurationSeconds": 1700,
  "gameVersion": "16.4.521",
  "timelineIngested": false,
  "participantCount": 10,
  "expectedParticipantCount": 10,
  "queueKnown": true,
  "hasLanes": true,
  "issues": ["missingTimeline"],
  "teams": [
    {
      "teamId": 100, "playerCount": 5, "expectedPlayerCount": 5, "unplacedCount": 0, "win": true,
      "slots": [
        { "position": "TOP", "filled": true, "participantId": 1, "championId": 122, "summonerName": "Player1", "win": true, "duplicateChampion": false }
      ]
    }
  ]
}
```

## `POST /ops/accounts/seed`

Injecte un compte dans le pipeline par son Riot ID. Idempotent (une requête
non traitée existante pour le même Riot ID + plateforme est renvoyée telle quelle).

**Body** — `SeedAccountRequest`

```json
{ "gameName": "Faker", "tagLine": "KR1", "platformId": "KR" }
```

**Réponse `202`** — `SeedRequestAcceptedResponse` · **`400`** si name/tag manquant
ou plateforme inconnue.

```json
{ "id": "9f8e7d6c-5b4a-3210-fedc-ba9876543210", "status": "Pending", "created": true }
```

`created` = `false` si une requête non traitée existait déjà (idempotence).
Le client poll ensuite `GET /ops/accounts/seed/{id}`.

## `GET /ops/accounts/seed/{id}`

État d'une requête de seed.

**Path** — `id` (GUID)

**Réponse `200`** — `SeedRequestReadModel` · **`404`** si inconnue.

```json
{
  "id": "9f8e7d6c-5b4a-3210-fedc-ba9876543210",
  "gameName": "Faker",
  "tagLine": "KR1",
  "platformId": "KR",
  "status": "Ingested",
  "error": null,
  "requestedAtUtc": "2026-06-26T10:00:00Z",
  "processedAtUtc": "2026-06-26T10:03:00Z",
  "resolvedPuuid": "abcdef0123456789…",
  "resolvedRiotAccountId": "11112222-3333-4444-5555-666677778888"
}
```

`status` ∈ `Pending` / `Resolving` / `Ingested` / `Failed`.

## `GET /ops/accounts/seed`

Requêtes de seed récentes, récentes d'abord.

**Query** — `status` (nom `SeedRequestStatus`), `search` (substring Riot ID),
`limit` (int) — tous optionnels.

**Réponse `200`** — `SeedRequestReadModel[]` (même forme que ci-dessus).

## `GET /ops/candidates`

Candidats « main » du pipeline (New → Scored → Queued → Processing → Validated,
ou Rejected), paginés.

**Query**

| Param      | Type | Requis | Description |
|------------|------|--------|-------------|
| `status`   | string | non | Un `MainCandidateStatus` (new/scored/queued/processing/validated/rejected). |
| `region`   | string | non | PlatformId (ex. `EUW1`). |
| `search`   | string | non | Riot ID / PUUID / champion-id. |
| `page`     | int  | non | 1-based. |
| `pageSize` | int  | non | Clamp [1, 100], défaut 25. |

**Réponse `200`** — `CandidatesReadModel`

```json
{
  "candidates": [
    {
      "id": "aaaa1111-2222-3333-4444-555566667777",
      "platformId": "EUW1",
      "puuid": "abcdef0123456789…",
      "gameName": "SomeMain",
      "tagLine": "EUW",
      "championId": 64,
      "championPoints": 850000,
      "championRankInMasteryTop": 1,
      "score": 0.92,
      "status": "Validated",
      "discoveredAtUtc": "2026-06-20T08:00:00Z",
      "scoredAtUtc": "2026-06-20T08:05:00Z",
      "validatedAtUtc": "2026-06-20T08:30:00Z",
      "lastPlayTimeUtc": "2026-06-19T22:10:00Z"
    }
  ],
  "total": 10400,
  "page": 1,
  "pageSize": 25
}
```

`gameName`/`tagLine` `null` tant que le compte n'est pas résolu.

## `GET /ops/candidates/{id}`

Détail d'un candidat : champs pipeline + identité jointe + nombre de matchs
ingérés + requête de seed liée (si origine manuelle).

**Path** — `id` (GUID)

**Réponse `200`** — `CandidateDetailReadModel` · **`404`** si inconnu.

```json
{
  "id": "aaaa1111-2222-3333-4444-555566667777",
  "platformId": "EUW1",
  "puuid": "abcdef0123456789…",
  "gameName": "SomeMain",
  "tagLine": "EUW",
  "championId": 64,
  "championPoints": 850000,
  "championRankInMasteryTop": 1,
  "score": 0.92,
  "status": "Validated",
  "discoveredAtUtc": "2026-06-20T08:00:00Z",
  "scoredAtUtc": "2026-06-20T08:05:00Z",
  "validatedAtUtc": "2026-06-20T08:30:00Z",
  "lastPlayTimeUtc": "2026-06-19T22:10:00Z",
  "ingestedMatchCount": 320,
  "seedRequest": {
    "id": "9f8e7d6c-5b4a-3210-fedc-ba9876543210",
    "gameName": "SomeMain", "tagLine": "EUW", "platformId": "EUW1",
    "status": "Ingested", "error": null,
    "requestedAtUtc": "2026-06-19T07:55:00Z", "processedAtUtc": "2026-06-19T07:58:00Z",
    "resolvedPuuid": "abcdef0123456789…", "resolvedRiotAccountId": "11112222-3333-4444-5555-666677778888"
  }
}
```

`seedRequest` est `null` quand le candidat a été découvert organiquement (ladder).

---

## Récapitulatif

| Groupe     | Endpoints | Auth        |
|------------|-----------|-------------|
| Champions  | 9         | Public      |
| Truemains  | 8         | Public      |
| Ops        | 17        | `X-Ops-Key` |
| Infra      | 4         | —           |
