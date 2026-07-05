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

## `GET /champions/{championId}/trend`

Évolution winrate/pickrate sur les ~5 derniers patchs, pour une position.
Volontairement **cross-patch** (ignore tout filtre de patch).

**Query** — `position` (string, optionnel ; défaut = lane dominante du champion)

**Réponse `200`** — `ChampionTrendReadModel` (toujours `200`, série possiblement vide)

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "points": [
    { "patch": "16.1", "winRate": 0.51, "pickRate": 0.061, "games": 1500 },
    { "patch": "16.2", "winRate": 0.52, "pickRate": 0.066, "games": 1620 },
    { "patch": "16.4", "winRate": 0.533, "pickRate": 0.072, "games": 1840 }
  ]
}
```

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

## `GET /champions/{championId}/timeline-leads`

Avance moyenne vs l'adversaire de lane à chaque palier (5/10/15/20/30 min) :
diffs gold / CS / kills / niveau / xp / dégâts.

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel)

**Réponse `200`** — `ChampionTimelineLeadsResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "intervals": [
    { "intervalMinute": 10, "games": 1200, "goldDiff": 145.3, "csDiff": 4.1, "killsDiff": 0.3, "levelDiff": 0.2, "xpDiff": 210.5, "damageDiff": 540.0 },
    { "intervalMinute": 15, "games": 1100, "goldDiff": 310.7, "csDiff": 6.8, "killsDiff": 0.6, "levelDiff": 0.4, "xpDiff": 380.2, "damageDiff": 1320.0 }
  ]
}
```

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

Courbe de puissance par minute (relative à l'adversaire) + événements (items
complétés, paliers de niveau 6/11/16) avec leur magnitude de spike.

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel)

**Réponse `200`** — `ChampionPowerspikesResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "curve": [
    { "minute": 5, "power": 0.05, "games": 1700 },
    { "minute": 10, "power": 0.22, "games": 1650 },
    { "minute": 15, "power": 0.41, "games": 1500 }
  ],
  "events": [
    { "type": "item", "refId": 6653, "avgMinute": 16.3, "spikeMagnitude": 0.18, "games": 1500 },
    { "type": "level", "refId": 6, "avgMinute": 6.8, "spikeMagnitude": 0.09, "games": 1700 }
  ]
}
```

- `curve[].power` : indice unitless (0 = à égalité avec l'adversaire, positif = devant).
- `events[].type` : `item` (`refId` = item id) ou `level` (`refId` = 6/11/16).

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
      ]
    }
  ],
  "page": 1,
  "pageSize": 25,
  "total": 480
}
```

- `ranked.score` : clé de tri SQL exposée. `ranked` peut être `null` (trié en dernier).
- `stats.wins`/`losses`/`winRate`/`kda` peuvent être `null` si aucune game attribuée.

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
  "positions": [
    { "position": "MIDDLE", "games": 300, "rate": 0.72 },
    { "position": "TOP", "games": 116, "rate": 0.28 }
  ]
}
```

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
        "lpDelta": null, "isMvp": true, "isAce": false
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
