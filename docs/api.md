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
- **Elo bracket** : un paramètre `eloBracket` accepte un palier nu (`GOLD` = ce
  palier seulement) ou la forme cumulative `TIER_PLUS` (`GOLD_PLUS` = ce palier et
  au-dessus), parmi `IRON`, `BRONZE`, `SILVER`, `GOLD`, `PLATINUM`, `EMERALD`,
  `DIAMOND`, `MASTER`, `GRANDMASTER`, `CHALLENGER`, plus `ALL`. `UNRANKED` n'est
  **pas** un palier valide malgré son apparence — `EloBracket.Ladder` l'exclut
  explicitement, donc `?eloBracket=UNRANKED` est une valeur non reconnue comme
  une autre. Une valeur non reconnue est traitée comme `ALL` (aucun filtre) plutôt que rejetée —
  elle restreint l'échantillon, elle ne change pas le sens de la question posée.
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

Annuaire des champions : une ligne par couple `(champion, position)` pour un patch,
mais **seulement pour les lanes dominantes** du champion (#1082).

Le jeu de lignes est filtré deux fois avant d'être renvoyé : les lignes sous le
plancher d'échantillon (`Champions:MinSampleGames`) tombent, puis chaque champion est
réduit à sa lane principale, plus une seconde lane uniquement si elle porte au moins
`MinSecondaryLanePlayRate` de ses games, dans la limite de `MaxLanesPerChampion`
(`ChampionDominantLaneFilter`). Un champion joué sur cinq lanes sort donc **2 lignes**
au plus, pas cinq — une lane résiduelle n'est pas une information de meta.

**Query**

| Param        | Type   | Requis | Défaut        | Description |
|--------------|--------|--------|---------------|-------------|
| `patch`      | string | non    | dernier patch | Filtre patch (`16.4`). Omis → dernier patch global. |
| `eloBracket` | string | non    | `ALL`         | Filtre de palier (`GOLD`, `GOLD_PLUS`…). |

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
    "tierScore": 0.71,
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
- `tier` : `S`/`A`/`B`/`C`/`D`, percentile relatif au patch courant, calculé
  **par position** (un S mid est S parmi les mids, pas face au patch entier).
- `tierScore` : le score mixé (#971) qui a placé la ligne dans son `tier`, scopé à
  la même position. C'est exactement celui que calcule `GET /champions/tierlist`
  pour la même ligne à `(patch, eloBracket)` égal — les deux endpoints ne peuvent
  donc pas se contredire, et trier cet annuaire dessus reproduit l'ordre du meta.
- `topBuild` : build dominant (résumé) ; `null` si aucun pattern observé.

## `GET /champions/tierlist`

Meta / tier-list d'un patch : les lignes `(champion, position)` réparties en
S/A/B/C/D par un mélange pickRate + banRate + winRate (#971, orienté présence :
pick et ban pèsent plus qu'un winrate calculé sur un échantillon étroit). Le
classement est établi **indépendamment par position** — un S mid est S parmi les
mids.

Il lit exactement le même jeu de lignes que `GET /champions`, donc le filtre de lanes
dominantes décrit ci-dessus s'applique aussi : une lane résiduelle d'un champion
n'apparaît dans aucun tier.

**Query**

| Param        | Type   | Requis | Défaut         | Description |
|--------------|--------|--------|----------------|-------------|
| `patch`      | string | non    | patch actif    | Filtre patch. |
| `position`   | string | non    | toutes         | Restreint à une voie. Non reconnue → `400`. |
| `eloBracket` | string | non    | `ALL`          | Filtre de palier. |

**Réponse `200`** — `ChampionTierListReadModel` (toujours `200`, ensemble de
tiers possiblement vide)

```json
{
  "patchVersion": "16.4",
  "position": "MIDDLE",
  "tiers": [
    {
      "tier": "S",
      "entries": [
        { "championId": 103, "position": "MIDDLE", "games": 1840, "winRate": 0.533, "pickRate": 0.072, "banRate": 0.184 }
      ]
    }
  ]
}
```

- `position` est `null` quand l'appelant n'a pas filtré ; le classement reste
  calculé voie par voie dans ce cas.
- `tiers[]` est trié du plus fort au plus faible (S d'abord) et **les tiers vides
  sont omis** : un patch pauvre peut donc renvoyer moins de cinq groupes.
- `entries[]` est trié par le même score mixé qui a décidé du bucket.
- `banRate` est `null` sur un patch antérieur à la collecte des bans (#920). Le
  terme ban est alors **retiré du mélange** et son poids réparti sur le pickRate et
  le winrate, pour que les tiers restent comparables entre patchs avec et sans
  données de ban — jamais traité comme un ban rate de 0.

## `GET /champions/overview`

Instantané taille page d'accueil (#972) : le total « games analysées » plus une
courte tranche pré-triée des lignes les plus fortes du patch actif. Toujours le
patch actif, sans filtre — la home n'a ni sélecteur de patch ni sélecteur d'elo.

**Query**

| Param   | Type | Requis | Défaut | Description |
|---------|------|--------|--------|-------------|
| `limit` | int  | non    | 8      | Taille de la tranche, **clampée à [1, 20]** : une valeur hors bornes est ramenée, jamais rejetée. Une valeur **non numérique** (`?limit=abc`) échoue en revanche au model binding et renvoie `400` avant même d'entrer dans l'action — c'est le cas de toute valeur qui ne se lie pas au type déclaré, sur cet endpoint comme sur les autres. |

**Réponse `200`** — `ChampionOverviewReadModel`

```json
{
  "patchVersion": "16.4",
  "gamesAnalyzed": 4820000,
  "topRows": [
    { "championId": 103, "position": "MIDDLE", "tier": "S", "games": 1840, "winRate": 0.533, "pickRate": 0.072, "banRate": 0.184 }
  ]
}
```

- `gamesAnalyzed` est **tous patchs confondus** : c'est le volume mesuré depuis
  toujours, pas celui du patch. Un chiffre affiché sans qualificatif ne doit pas
  chuter à chaque rotation de patch. Il compte aussi les scopes sous le plancher et
  sans position, donc ce n'est **pas** la somme des `topRows` (une courte tranche).
- `patchVersion` est le patch que le site sert, pas forcément le plus récent avec
  des données : un patch trop mince pour remplir un annuaire est sauté (#1109).
- Lit la même entrée de cache qu'un `GET /champions` non filtré : les deux surfaces
  ne peuvent pas afficher deux chiffres différents, et la home ne paie pas un second
  calcul d'agrégat.

## `GET /champions/{championId}`

Page champion : onglets de build dominants pour un `(patch, position)`.

**Path** — `championId` (int)

**Query**

| Param        | Type   | Requis | Description |
|--------------|--------|--------|-------------|
| `patch`      | string | non    | Filtre patch. |
| `position`   | string | non    | Filtre position. Non reconnue → `400`. |
| `eloBracket` | string | non    | Filtre de palier (`GOLD`, `GOLD_PLUS`…). Défaut `ALL`. |

**Réponse `200`** — `ChampionResponse` · **`404`** si le champion n'a pas de scope.

```json
{
  "championId": 103,
  "patch": "16.4",
  "position": "MIDDLE",
  "eloBracket": "ALL",
  "eloCoverage": 1.0,
  "minSampleMet": true,
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
- `eloBracket` : le palier réellement résolu pour cette tranche (`ALL` par défaut).
- `eloCoverage` : part des games du champion sur ce `(patch, position)` que couvre
  le palier sélectionné, tous paliers confondus au dénominateur. Vaut `1.0` sur
  `ALL`. C'est ce qui permet de dire à quel point une tranche haut-elo est
  représentative au lieu de la présenter comme « le » build.
- `minSampleMet` : `false` quand `totalGames` est sous le plancher d'échantillon.
  La réponse **reste un `200` avec ses données** — le front les affiche en les
  marquant peu fiables plutôt que de masquer la page.

### Filtre matchup (`opponentChampionId`)

| Param | Type | Requis | Description |
|-------|------|--------|-------------|
| `opponentChampionId` | int | non | Adversaire de voie. Re-calcule **toutes** les sections build (variations, core, build tree, runes, skill order) sur les seules parties où les deux champions se sont affrontés. |

Exige `position` : le self-join apparie les deux camps dessus, donc un matchup sans voie est un **400**.
Renvoie **404** quand la fenêtre retenue ne contient aucune partie de ce matchup — distinct d'une réponse vide.

La réponse a exactement la même forme que sans le filtre. Deux différences de fond :

- Les données viennent d'un repli **live** de `match_participants` (les agrégats de patterns n'ont pas de
  dimension adversaire), borné à **un seul patch** — celui demandé, sinon le patch le plus récent sur lequel
  ce matchup a été joué — et plafonné à 2 000 parties. Un seul et pas « tout ce que la rétention garde » :
  une réponse qui mélangerait silencieusement deux patchs mettrait un chiffre sous une étiquette qui ne le
  décrit pas.
- L'échantillon est petit par nature — mesuré en prod, la paire médiane champion × adversaire × position tient
  **4 parties** sur un patch. Chaque variation porte donc son `games`, et le front l'affiche.

## `GET /champions/{championId}/trend`

Évolution winrate/pickrate/banrate sur les ~5 derniers patchs, pour une position.
Volontairement **cross-patch** (ignore tout filtre de patch).

**Query** — `position` (string, optionnel ; défaut = lane dominante du champion).
Une valeur non vide qui ne canonicalise pas est un **`400`** : elle n'est pas
silencieusement ramenée à « pas de filtre ».

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

## `GET /champions/{championId}/patch-diff`

Ce qui a changé pour un champion entre deux patchs (#534) : l'écart de winrate,
plus les mouvements du premier item, de la page de runes et de l'ordre de skills
dominants, sur une position.

**Query**

| Param      | Type   | Requis | Défaut | Description |
|------------|--------|--------|--------|-------------|
| `from`     | string | non    | —      | Patch le plus ancien. Omis → résolu par le service (voir ci-dessous). |
| `to`       | string | non    | —      | Patch le plus récent. Omis → résolu par le service (voir ci-dessous). |
| `position` | string | non    | lane dominante | Non reconnue → `400`. |

Résolution des bornes, en trois branches — un côté explicite **ancre** la paire, il
n'est jamais écarté au profit des deux derniers patchs :

- **Les deux omis** → les deux patchs les plus récents ayant des données pour la voie
  résolue.
- **`from` seul** → `to` devient le patch de la voie immédiatement **plus récent** que
  `from`.
- **`to` seul** → `from` devient le patch immédiatement **plus ancien** que `to`.

Un patch explicitement demandé est honoré tel quel **même s'il n'a aucune donnée** pour
la voie : le côté correspondant sort `null`. Comparer les deux derniers patchs à la
place transformerait une question précise en une autre question, sans le dire.

**Réponse `200`** — `ChampionPatchDiffReadModel`. Toujours `200`, même à moitié
vide : un patch où le champion n'a jamais été joué donne simplement un côté `null`,
et la page rend son propre état « pas assez de données » plutôt qu'un `404`.

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "availablePatchCount": 5,
  "from": {
    "patch": "16.3",
    "games": 1620, "wins": 842, "winRate": 0.5198,
    "itemPath": { "itemIds": [6653, 3020, 3157], "games": 540, "pickRate": 0.60, "winRate": 0.56 },
    "runePage": { "primaryStyleId": 8200, "primaryKeystoneId": 8214, "…": "page complète" },
    "skillOrder": { "sequence": ["Q", "E", "W"], "games": 480, "pickRate": 0.53, "winRate": 0.57 }
  },
  "to": { "patch": "16.4", "games": 1840, "wins": 981, "winRate": 0.5332, "itemPath": null, "runePage": null, "skillOrder": null },
  "delta": {
    "winRateChange": 0.0134,
    "firstItemChanged": true,
    "keystoneChanged": false,
    "skillOrderChanged": false
  }
}
```

- `availablePatchCount` : nombre de patchs distincts avec des données pour ce
  `(champion, position)`. Le front masque la section sous 2 — un champion à patch
  unique ne peut se comparer qu'à lui-même.
- `position` vaut la chaîne vide quand le champion n'a de scope positionné sur
  aucun des deux patchs (les deux côtés sont alors `null` eux aussi).
- `delta` est `null` dès qu'un côté manque : un écart n'a de sens qu'avec ses deux
  bornes. `winRateChange` est signé (`to − from`).
- `itemPath` / `runePage` / `skillOrder` sont les entrées **exactes** que sert la
  lecture des builds pour le même patch + position — le diff et la page de build ne
  peuvent pas diverger, et le front les rend avec les mêmes composants.

## `GET /champions/{championId}/matchups`

Matchups de lane : chaque adversaire direct rencontré au-dessus d'un plancher de
games, avec games / wins / winrate.

Servi depuis l'agrégat pré-calculé `champion_matchup_stats` (#606), jamais recalculé
en live : cette route est globale, et c'est seulement la route scopée joueur
(`GET /truemains/{nameTag}/champions/{championId}/matchups`) qui fait le self-join à
la demande.

**Query**

| Param        | Type | Requis | Description |
|--------------|------|--------|-------------|
| `position`   | string | **oui** | Position Riot. Non reconnue → `400`. |
| `patch`      | string | non | Filtre patch. Omis → tous patchs. |
| `eloBracket` | string | non | Filtre de palier. Défaut `ALL`. |
| `opponent`   | int (≥1) | non | Restreint à un seul adversaire (plancher = 1 game). |

**Réponse `200`** — `ChampionMatchupsResponse` (liste triée par winRate décroissant)

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "matchups": [
    {
      "opponentChampionId": 1,
      "games": 60, "wins": 38, "winRate": 0.633,
      "playRate": 0.041,
      "winRateLowerBound": 0.507, "winRateUpperBound": 0.741,
      "laneWinRate": 0.58, "decidedLaneGames": 31,
      "averageGoldDiffAt15": 412.5, "goldDiffLaneGames": 44,
      "averageXpDiffAt15": 168.0, "xpDiffLaneGames": 44
    }
  ]
}
```

- `patch` est `null` quand aucun patch n'a été épinglé.
- `playRate` : part que cet adversaire représente dans les games de matchup du
  champion sur la même tranche, **avant** tout plancher — « tu croises ce champion
  dans 4 % de tes parties » plutôt qu'un compte brut livré à l'interprétation. Il
  est toujours renseigné, y compris avec `opponent`, sur cette route comme sur la
  route **scopée joueur** : le dénominateur vient d'un comptage séparé sur tout le
  champ d'adversaires du joueur, avant filtrage sur `opponent` et avant le plancher
  du leaderboard — jamais des seules lignes renvoyées.
- `winRateLowerBound` / `winRateUpperBound` : bornes de l'intervalle de Wilson à
  95 %. La liste des **meilleurs** matchups se trie sur la borne basse et celle des
  **pires** sur la borne haute — trier sur le winrate brut ferait du classement un
  détecteur de petits échantillons (82 % sur 11 parties n'établit rien).
- `laneWinRate` : part des lanes *décidées* (avance d'or au-delà du seuil à 15 min,
  #919) gagnées. Dénominateur = `decidedLaneGames`, pas `games` : une partie sans
  timeline ou finie avant 15 min ne se juge pas, et une lane dans la bande n'a été
  ni gagnée ni perdue. **`null` quand rien ne peut être dit** — moins de lanes
  décidées que le plancher, ou tranche scopée joueur — jamais un 0 de substitution.
- `averageGoldDiffAt15` / `averageXpDiffAt15` : l'ampleur que le taux ne porte pas
  (une lane gagnée 60 % du temps à +120 or et une à +1200 or, c'est le même taux et
  deux matchups différents). Chacun a **son propre dénominateur**
  (`goldDiffLaneGames`, `xpDiffLaneGames`) : les lignes repliées avant #976/#1111 ne
  portent pas ces mesures, et emprunter l'autre dénominateur transformerait une
  absence de données en lane parfaitement égale. `null` = jamais mesuré, jamais `0`.

## `GET /champions/{championId}/synergies`

Meilleurs duos (#922) : pour chaque coéquipier assez souvent joué avec ce
champion, les parties partagées, le winrate de la paire et — la valeur qui classe
la liste — la **synergie**, c'est-à-dire l'écart entre ce winrate et celui que les
deux champions pris séparément laissaient prévoir. Le winrate brut d'une paire ne
fait pour l'essentiel que redire à quel point les deux champions sont bons seuls,
ce à quoi sert déjà la tier-list.

**Query**

| Param             | Type   | Requis | Description |
|-------------------|--------|--------|-------------|
| `position`        | string | **oui** | Position du champion. Non reconnue → `400`. |
| `partnerPosition` | string | non | Restreint à une voie de partenaire. Non reconnue → `400`. |
| `patch`           | string | non | Filtre patch. Omis → tous patchs avec données. |
| `eloBracket`      | string | non | Filtre de palier. Défaut `ALL`. |

**Réponse `200`** — `ChampionSynergiesResponse` (toujours `200`, liste possiblement vide)

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "partnerPosition": null,
  "minGames": 20,
  "championGames": 1840,
  "championWinRate": 0.533,
  "cohortWinRate": 0.512,
  "partners": [
    {
      "partnerChampionId": 64,
      "partnerPosition": "JUNGLE",
      "games": 96, "wins": 58, "winRate": 0.604,
      "playRate": 0.052,
      "partnerBaselineGames": 4200, "partnerBaselineWinRate": 0.521,
      "expectedWinRate": 0.541,
      "synergy": 0.063
    }
  ]
}
```

- `partners[]` est trié par `synergy` décroissante (meilleur partenaire d'abord).
- `minGames` et `championGames` sont renvoyés pour **expliquer une liste vide**
  plutôt que de laisser lire « ce champion n'a aucune synergie » : un champion dont
  l'échantillon est trop mince pour établir un winrate attendu ne renvoie aucune
  entrée au lieu d'en inventer. `championGames` à 0 ⇒ `partners` nécessairement vide.
- `cohortWinRate` : winrate de toute la cohorte suivie sur la tranche, publié parce
  que c'est lui qui rend les valeurs attendues reproductibles — un partenaire pile
  dessus n'apporte rien à l'espérance.
- `partnerBaselineWinRate` est le winrate **marginal** du partenaire (à quelle
  fréquence l'équipe d'un joueur suivi gagne avec ce champion sur cette voie, tous
  partenaires confondus), délibérément pas son winrate solo de main : cette
  population-là est spécialiste de son champion, un partenaire est qui s'est
  présenté, et mélanger les deux biaise toutes les espérances vers le haut.
- `expectedWinRate` combine les deux marginaux et la référence de cohorte en
  log-odds ; `synergy` = `winRate − expectedWinRate`.

## `GET /champions/{championId}/synergies/trios`

Troisième pick pour un duo déjà choisi (#922) : restreint aux parties où ce
champion et `partner` ont réellement joué ensemble, les coéquipiers dont le trio
sur- ou sous-performe ce que les trois winrates individuels prédisaient.

**Query**

| Param             | Type     | Requis | Description |
|-------------------|----------|--------|-------------|
| `position`        | string   | **oui** | Position du champion. Non reconnue → `400`. |
| `partner`         | int (≥1) | **oui** | Champion du partenaire. |
| `partnerPosition` | string   | **oui** | Voie du partenaire. Non reconnue → `400`. |
| `patch`           | string   | non | Filtre patch. |
| `eloBracket`      | string   | non | Filtre de palier. Défaut `ALL`. |

`partnerPosition` **doit différer** de `position` : une équipe n'aligne pas deux
joueurs sur une voie, donc la requête renverrait éternellement une liste vide.
Elle est rejetée en `400`, ce qui nomme l'erreur.

**Réponse `200`** — `ChampionTrioSynergiesResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "partnerChampionId": 64,
  "partnerPosition": "JUNGLE",
  "patch": "16.4",
  "minGames": 5,
  "pairGames": 96, "pairWins": 58, "pairWinRate": 0.604,
  "completions": [
    {
      "championId": 12, "position": "UTILITY",
      "games": 14, "wins": 10, "winRate": 0.714,
      "baselineGames": 3100, "baselineWinRate": 0.508,
      "expectedWinRate": 0.556, "synergy": 0.158
    }
  ]
}
```

- Contrairement au duo, le trio **n'est pas pré-agrégé** : l'espace des triples est
  bien plus grand et presque entièrement vide. Il est calculé à la demande sur
  `match_participants`, d'abord réduit aux parties du duo — la requête est donc
  bornée par le nombre de parties de la paire, pas par celui du champion.
- Conséquence à connaître : `pairGames` est compté sur les **matchs encore
  retenus**, alors que l'endpoint duo lit un agrégat qui garde aussi les patchs
  anciens gelés. Les deux décrivent la même paire sur deux fenêtres différentes et
  leurs compteurs ne coïncideront pas — d'où `pairGames` renvoyé explicitement.
- Une liste `completions` vide est la réponse **normale** pour un duo trop peu joué
  pour se découper une troisième fois, pas une erreur ; `pairGames` et `minGames`
  permettent de dire exactement cela.

## `GET /champions/{championId}/scaling`

Winrate en fonction de la durée de game, plus un indice de scaling (winrate des
games longues − winrate des games courtes ; positif = scale late).

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel),
`eloBracket` (optionnel)

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

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel),
`eloBracket` (optionnel)

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

**Query** — `position` (**requis**, `400` sinon), `patch` (optionnel),
`eloBracket` (optionnel)

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
`eloBracket` (optionnel), `opponentChampionId` (optionnel)

Le couple `buildFirstItemId` / `buildKeystoneId` identifie le build core de la
même façon que la lecture des builds clé ses onglets — un build jamais joué
renvoie une liste vide plutôt qu'un repli sur les autres builds du champion.

`opponentChampionId` restreint les spikes aux parties jouées contre cet
adversaire de lane, le même filtre que `GET /champions/{id}` (#957). Le plancher
de parties ne s'applique alors pas : un matchup tient 4 parties en médiane sur un
patch, donc chaque événement porte son propre `games` plutôt que d'être masqué.
Seules les parties agrégées depuis #957 portent un adversaire — un matchup non
encore couvert renvoie une liste vide, ce qui reste un `200`.

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

## `GET /champions/{championId}/mains-comparison`

Face-à-face entre un compte Riot et les mains de ce champion (#528) : winrate,
KDA, CS/min et or côte à côte, sur la même queue, le même patch et la même voie.

**Query**

| Param      | Type   | Requis | Description |
|------------|--------|--------|-------------|
| `account`  | string | **oui** | Riot ID tel qu'un joueur l'écrit (`Name#TAG` ; le slug `Name-TAG` est accepté). |
| `main`     | string | non | Restreint la colonne de droite à un seul compte suivi. Omis → agrège tous les mains du champion. |
| `position` | string | non | Filtre voie. Non reconnue → `400`. |
| `patch`    | string | non | Filtre patch. Omis → tous patchs encore retenus. |

**Deux modes d'échec volontairement distincts :**

- Un Riot ID **mal formé** — absent, vide, sans séparateur, une moitié vide, trop
  long — est un **`400`**. C'est une entrée invalide, pas une réponse sur un joueur.
- Un Riot ID bien formé dont nous n'avons **aucune ligne** est un **`200`** portant
  `status: "UNKNOWN_ACCOUNT"` (ou `UNKNOWN_TARGET` pour `main`). La comparaison ne
  couvre que les comptes déjà en base — il n'y a aucun appel Riot à la volée — donc
  « nous ne détenons pas ce joueur » est une réponse normale ici, pas un échec.

**Réponse `200`** — `ChampionMainsComparisonResponse`

```json
{
  "championId": 103,
  "patch": "16.4",
  "position": "MIDDLE",
  "minGames": 10,
  "status": "OK",
  "player": {
    "identity": { "gameName": "Faker", "tagLine": "KR1", "platformId": "KR", "profileIconId": 6, "summonerLevel": 540 },
    "players": 1,
    "games": 42, "wins": 25, "winRate": 0.595,
    "kills": 7.4, "deaths": 3.1, "assists": 8.2, "kda": 5.03,
    "csPerMin": 8.1, "goldPerMin": 452.0, "goldPerGame": 13800.0,
    "sampleMet": true
  },
  "mains": { "identity": null, "players": 47, "games": 1840, "…": "mêmes champs", "sampleMet": true }
}
```

- `status` ∈ `OK` / `UNKNOWN_ACCOUNT` / `UNKNOWN_TARGET` / `INSUFFICIENT_SAMPLE`.
  `INSUFFICIENT_SAMPLE` renvoie **quand même les deux colonnes** avec leurs vrais
  compteurs, pour que l'appelant puisse dire laquelle est mince.
- `minGames` : plancher (`ChampionsList:MinComparisonGames`), exposé pour dire à
  quelle distance de la barre est un échantillon mince plutôt que masquer le panneau.
- `player` n'est `null` que si le Riot ID est inconnu : un compte connu sans partie
  sur le champion donne une colonne à zéro game.
- `mains` agrège par défaut **tous les mains suivis du champion, le compte comparé
  exclu**. Un `main` ciblé est résolu comme *n'importe quel* compte détenu — il
  n'est pas exigé qu'il soit flaggé main de ce champion, pour qu'on puisse se
  mesurer à un rival précis ; seule la colonne par défaut est restreinte aux mains.
- Les statistiques de comptage sont des **moyennes par partie**, pour qu'un joueur
  seul et un pool de mains restent sur la même échelle.

## `POST /champions/{championId}/composition-build`

Recommandation de build pour une draft (éventuellement partielle) : le champion du
joueur (dans la route), sa position, et les picks alliés/ennemis connus. `POST`
parce que l'entrée — jusqu'à neuf slots champion/position — est trop riche pour des
query params.

**Body** — `CompositionBuildRequest`

```json
{
  "position": "MIDDLE",
  "patch": "16.4",
  "eloBracket": "GOLD_PLUS",
  "allies": [ { "championId": 64, "position": "JUNGLE" } ],
  "enemies": [ { "championId": 157, "position": "MIDDLE" } ]
}
```

- `position` est **requise** (`400` sinon). `allies` / `enemies` sont optionnelles et
  peuvent être partielles ou nulles.
- `400` également pour : un `championId` de route ou de slot non positif, une
  position de slot non reconnue, deux slots d'une même équipe sur la même position,
  ou un slot allié posé sur la position du joueur (ce slot est le champion de la route).
- La composition **classe** les parties historiques, elle ne filtre jamais durement :
  une draft pauvre dégrade vers les parties récentes du champion à la position, et le
  bloc `confidence` le dit. Seul l'adversaire de voie fait exception (voir
  `matchupRequested` ci-dessous).

**Réponse `200`** — `CompositionBuildResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "eloBracket": "GOLD_PLUS",
  "matchupRequested": true,
  "matchupFound": true,
  "confidence": {
    "sampleSize": 120,
    "candidatePoolSize": 2000,
    "truemainGameCount": 88,
    "maxPossibleScore": 12,
    "meanSimilarity": 0.41
  },
  "lane": {
    "measuredGames": 96,
    "decidedGames": 51,
    "winRate": 0.58,
    "averageGoldDiffAt15": 380.2,
    "averageXpDiffAt15": 145.0
  },
  "build": {
    "gamesConsidered": 120,
    "wins": 68,
    "runePage": { "…": "même forme que la page champion" },
    "starterItems": { "itemIds": [1056, 2003], "games": 96, "pickRate": 0.80, "winRate": 0.57 },
    "boots": { "itemIds": [3020], "games": 90, "pickRate": 0.75, "winRate": 0.56 },
    "corePath": { "itemIds": [6653, 3157, 3089], "games": 54, "pickRate": 0.45, "winRate": 0.59 },
    "summonerSpells": { "spell1Id": 4, "spell2Id": 14, "games": 80, "pickRate": 0.66, "winRate": 0.57 },
    "skillOrder": { "sequence": ["Q", "E", "W"], "games": 62, "pickRate": 0.52, "winRate": 0.58 },
    "firstItemId": 6653,
    "buildTree": []
  }
}
```

- `matchupRequested` : la draft a épinglé l'adversaire de voie — le matchup est
  alors une **exigence dure** sur les parties échantillonnées, pas un signal de tri.
  `matchupFound` est `false` uniquement quand aucune partie enregistrée ne porte ce
  matchup : le build est alors vide et le client retombe sur le build de base.
- `confidence` : `sampleSize` = parties réellement agrégées, `candidatePoolSize` =
  parties examinées (borné par le plafond configuré), `meanSimilarity` ∈ [0, 1] vaut
  0 quand aucun slot n'a été fourni. Une donnée pauvre doit se lire pauvre, jamais
  comme une certitude fabriquée.
- `lane` est mesurée sur **exactement les parties que compte `confidence`**, avec
  trois dénominateurs distincts parce que ce sont trois questions : combien de
  parties étaient jugeables (`measuredGames`), combien ont été tranchées
  (`decidedGames`), et sur quoi porte la moyenne des écarts (`measuredGames`, nuls
  compris). `winRate` est `null` — jamais `0` — quand rien n'a été tranché.
- Chaque dimension de `build` est **nullable** : un top-K pauvre (timeline manquante,
  pas de sélection de runes) laisse tomber la dimension au lieu d'en inventer une.

## `POST /champions/{championId}/composition-build/games`

Les parties dont la recommandation ci-dessus a été calculée, une page à la fois,
dans l'ordre propre de la sélection (mains d'abord, puis similarité, la récence
départageant).

**Body** — `CompositionBuildRequest`, **identique** à celui de la recommandation :
la draft *est* l'identité de la sélection, donc les deux répondent forcément sur le
même échantillon. Mêmes `400` que ci-dessus.

**Query**

| Param      | Type | Requis | Défaut | Description |
|------------|------|--------|--------|-------------|
| `page`     | int  | non    | 1      | 1-based (clampé ≥ 1). |
| `pageSize` | int  | non    | 10     | 0/omis → 10 ; plafonné à 25. |

Route séparée parce que la page matchup refetch le build à **chaque** édition de la
draft : hydrater des lignes de match que personne n'a ouvertes serait payé par tout
le monde (#940).

**Réponse `200`** — `CompositionBuildGamesResponse`

```json
{
  "championId": 103,
  "position": "MIDDLE",
  "patch": "16.4",
  "page": 1,
  "pageSize": 10,
  "total": 120,
  "maxPossibleScore": 12,
  "games": [
    {
      "score": 8,
      "isTruemain": true,
      "pilot": { "gameName": "Faker", "tagLine": "KR1", "profileIconId": 6 },
      "match": { "…": "MatchSummaryReadModel, même forme que le fil de matchs" }
    }
  ]
}
```

- `total` = les parties sélectionnées toutes pages confondues, c'est-à-dire
  l'échantillon de la recommandation.
- `maxPossibleScore` est le dénominateur de `score`. Il vaut `0` quand la draft ne
  portait aucun slot : chaque partie score alors 0 et le ratio est **indéfini**, pas
  0 %.
- `pilot` est `null` quand le participant ne porte pas de compte Riot résolu (des
  lignes harvestées peuvent précéder la résolution des comptes).

---

# Truemains — `/truemains`

Public. Profils de joueurs, leaderboard, historique. `429` sous rate-limit.

**`{nameTag}` est le slug `Name-TAG`**, pas le Riot ID tel qu'on le tape. Le séparateur
est le **dernier `-`** du segment, ce qui laisse un nom de jeu contenir des tirets
(`Some-Player-EUW1` → `Some-Player` / `EUW1`). Un `Name#TAG` percent-encodé ne contient
aucun `-` : il ne parse pas et la route répond `404`. La forme tapée `Name#TAG` n'est
acceptée que par les endpoints qui prennent un Riot ID en **paramètre de requête**
(la comparaison aux mains, l'explorateur de comptes), pas en segment de route.

## `GET /truemains/search`

Lookup name/tag pour la barre de recherche.

**Query**

| Param   | Type   | Requis | Description |
|---------|--------|--------|-------------|
| `q`     | string | non    | Nom partiel, ou Riot ID complet `Name#TAG`. Moins de 2 ou plus de 64 caractères → liste vide, sans requête (une barre de recherche ne balaye pas la table sur une lettre). |
| `limit` | int    | non    | Max résultats. Omis/≤0 → 10, plafonné à 25. |

**Réponse `200`** — `SearchResponse` (toujours `200`, liste possiblement vide)

```json
{
  "results": [
    {
      "identity": { "gameName": "Faker", "tagLine": "KR1", "platformId": "KR", "profileIconId": 6, "summonerLevel": 540 },
      "region": "korea",
      "ranked": { "tier": "CHALLENGER", "division": "I", "leaguePoints": 1245 },
      "topChampionIds": [103, 157, 245],
      "positions": { "primary": "MIDDLE", "secondary": "TOP" }
    }
  ]
}
```

- `region` ∈ `europe` / `americas` / `korea`. `ranked` est `null` sans snapshot.
- `topChampionIds` : jusqu'à 3 champions les plus joués (play rate décroissant),
  la même tranche que la ligne de leaderboard. **Liste vide** tant que l'analyse des
  mains n'a pas tourné.
- `positions` : voie principale et secondaire dérivées de la part de position sur
  les mains. `null` tant que l'analyse des mains n'a pas tourné (le front omet alors
  les icônes de rôle) ; `secondary` est `null` quand aucune seconde voie ne franchit
  le plancher de bruit.

## `GET /truemains`

Leaderboard paginé des truemains. Pose un en-tête
`Cache-Control: public, s-maxage=30, stale-while-revalidate=60`.

**Query**

| Param        | Type   | Requis | Défaut | Description |
|--------------|--------|--------|--------|-------------|
| `page`       | int    | non    | 1      | Page 1-based. Hors plage (< 1) → **`400`**. |
| `pageSize`   | int    | non    | défaut | Omis → taille par défaut. Hors [1, 50] → **`400`**. |
| `region`     | string | non    | toutes | `europe`/`americas`/`korea`. Valeur non reconnue **ignorée** (aucun filtre), jamais un `400` — contrairement aux routes scopées champion. |
| `position`   | string | non    | toutes | Filtre position. Valeur non reconnue également ignorée. |
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
        { "championId": 103, "games": 120, "playRate": 0.29, "isOtp": false, "primaryKeystoneId": 8214, "secondaryStyleId": 8100, "firstItemId": 6653 }
      ],
      "positions": { "primary": "MIDDLE", "secondary": "TOP" },
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
- `stats` mélange **deux dénominateurs**, à ne pas lire comme un tout. `wins`/`losses`/
  `winRate` viennent du dernier snapshot de rang, c'est-à-dire du **split ranked
  complet** du compte, indépendamment des champions suivis ; ils sont `null` quand ce
  snapshot ne porte pas ces totaux (les anciens ne les enregistraient pas). `games` ne
  compte que les parties attribuées aux mains suivis, et peut donc valoir `0` alors que
  `wins`/`losses` sont renseignés. `kda` est `null` quand aucune ligne n'est attribuée.
- `positions` : voie principale / secondaire, dérivées de la part de position sur les
  mains (même logique que le profil, #205). `null` quand aucune analyse des mains n'a
  tourné, pour que l'UI omette les icônes de rôle au lieu d'en inventer une.
- `topChampions[].primaryKeystoneId` / `secondaryStyleId` / `firstItemId` sont `null`
  quand aucun build agrégé n'existe pour le joueur sur ce champion.
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

Profil d'un joueur. `nameTag` est le slug `Name-TAG` (cf. l'entête de section).

**Réponse `200`** — `ProfileReadModel` · **`404`** si compte inconnu.

```json
{
  "identity": { "gameName": "Faker", "tagLine": "KR1", "platformId": "KR", "profileIconId": 6, "summonerLevel": 540 },
  "ranked": { "tier": "CHALLENGER", "division": "I", "leaguePoints": 1245, "wins": 240, "losses": 172, "winRate": 0.583 },
  "mains": [
    {
      "championId": 103, "games": 120, "playRate": 0.29,
      "primaryPosition": "MIDDLE", "isOtp": false,
      "isSampleRetired": false, "measuredAtUtc": "2026-06-26T04:00:00Z"
    }
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

- `dedication` porte sur le champion signature du joueur (son main le plus joué) ;
  `null` si aucun champion n'est encore classé comme main. Voir
  [`docs/dedication-score.md`](dedication-score.md).
- `mains[].isSampleRetired` : `true` quand les matchs ayant servi à calculer `games`
  et `playRate` sont sortis de la rétention (#1216). La ligne est **gardée
  volontairement** — le joueur reste au leaderboard — mais le chiffre décrit un
  échantillon que le site ne détient plus, donc l'UI le date avec `measuredAtUtc`
  au lieu de le présenter comme courant.

## `GET /truemains/{nameTag}/champions/{championId}`

Page champion **scopée au joueur** : même contrat que `GET /champions/{id}`, mais
agrégé uniquement sur les games de ce joueur.

**Query** — `patch` (optionnel), `position` (optionnel ; non reconnue → `400`)

**Réponse `200`** — `ChampionResponse` (même forme qu'au-dessus) ·
**`404`** si `nameTag` est malformé, si le compte est inconnu, ou s'il n'existe
**aucune tranche agrégée** pour ce joueur sur ce champion.

Un échantillon mince n'est **pas** un `404` : le plancher `MinPlayerGames` sert à
*choisir* le patch, pas à barrer la route. Quand aucun patch ne le franchit, le
service retombe sur le patch le plus récent ayant des parties et rend la tranche mince
telle quelle, avec `minSampleMet: false` — la page dit elle-même que c'est peu, ce
qu'un `404` ne permettrait pas.

Pas de filtre `eloBracket` ici : la tranche est déjà celle d'un joueur unique.

## `GET /truemains/{nameTag}/champions/{championId}/divergence`

« Toi vs les mains » : en quoi le starter, les bottes, le chemin d'items et l'ordre
de skills dominants de ce joueur diffèrent de ce que font les autres mains du
champion, au même patch et à la même position.

Pur calque sur les agrégats existants — le côté joueur lit sa tranche
`champion_aggregate_*`, le côté mains lit la même tranche sur tous les **autres**
comptes. Aucune table nouvelle, aucun scan de matchs en live.

**Query** — `patch` (optionnel), `position` (optionnel ; défaut = lane dominante du
joueur sur le champion, non reconnue → `400`)

**Réponse `200`** — `PlayerBuildDivergenceResponse` · **`404`** si `nameTag`
malformé, compte inconnu, ou aucun agrégat du tout pour ce joueur sur le champion.

Un joueur connu dont l'échantillon est trop mince — ou un couple champion + voie
avec trop peu d'autres mains pour servir de référence — est un **`200`** portant les
compteurs et **aucune dimension** : la page affiche un « pas encore assez de
parties » honnête au lieu d'un échec.

```json
{
  "championId": 103,
  "patch": "16.4",
  "position": "MIDDLE",
  "playerGames": 42,
  "mainsGames": 1798,
  "mainsPlayers": 46,
  "minPlayerGames": 5,
  "minMainsGames": 20,
  "minSampleMet": true,
  "referenceSampleMet": true,
  "dimensions": [
    {
      "dimension": "itemPath",
      "diverges": true,
      "player": { "itemIds": [6653, 3089, 3157], "skills": [], "games": 20, "pickRate": 0.48, "winRate": 0.55 },
      "mains": { "itemIds": [6653, 3157, 3089], "skills": [], "games": 1120, "pickRate": 0.62, "winRate": 0.57 },
      "mainsGamesOnPlayerChoice": 72,
      "mainsRateOnPlayerChoice": 0.04,
      "mainsWinRateOnPlayerChoice": 0.51
    }
  ]
}
```

- `dimension` ∈ `starterItems` / `boots` / `itemPath` / `skillOrder`. Ces valeurs
  font partie du contrat : le front branche ses libellés et ses icônes dessus.
- `patch` et `position` sont **résolus depuis la tranche du joueur** (le patch le
  plus récent où il a réellement des parties), puis épinglés côté mains, pour que les
  deux colonnes soient toujours comparables.
- `mainsGames` exclut les parties du joueur : « x % des mains » ne peut jamais
  vouloir dire en partie « x % de toi ». `mainsPlayers` dit sur combien de comptes
  repose la comparaison, plutôt que de laisser imaginer une foule qui serait trois
  personnes.
- `dimensions` est trié du plus actionnable au moins actionnable : les lignes qui
  divergent d'abord, puis par l'accord des mains entre eux (une dimension sur
  laquelle ils sont partagés est un conseil plus faible qu'une unanimité). Les lignes
  **qui ne divergent pas sont renvoyées aussi** — une carte qui ne liste que des
  erreurs se lit comme un réquisitoire, pas comme une comparaison.
- `mainsWinRateOnPlayerChoice` est `null` quand aucune partie de main n'a fait ce
  choix : ça évite de faire passer pour mauvais un choix simplement rare.
- Dans `player`/`mains`, exactement l'un de `itemIds` et `skills` est renseigné.

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
    { "kind": "KillParticipation", "weight": 14, "value": 0.66, "games": 14 },
    { "kind": "DamageShare", "weight": 18, "value": 0.71, "games": 14 },
    { "kind": "GoldShare", "weight": 7, "value": 0.55, "games": 14 },
    { "kind": "Farming", "weight": 14, "value": 0.63, "games": 14 },
    { "kind": "Vision", "weight": 5, "value": 0.40, "games": 14 },
    { "kind": "Laning", "weight": 10, "value": 0.58, "games": 12 },
    { "kind": "MidGame", "weight": 6, "value": 0.61, "games": 9 },
    { "kind": "Roam", "weight": 6, "value": 0.44, "games": 11 }
  ]
}
```

- `components` porte **toujours les neuf axes, dans l'ordre de l'énumération**, y
  compris ceux qui ont été écartés sur l'échantillon — ceux-là sortent avec
  `value: null`. Le tableau ne change donc jamais de longueur ni d'ordre d'un joueur
  à l'autre, et une composante absente est visiblement absente plutôt que silencieuse.
- `window` : nombre de parties les plus récentes prises en compte (métrique de
  forme, pas de carrière).
- `games` d'une composante ≤ `games` global : une partie sans couverture timeline
  est **exclue** de la moyenne de la composante au lieu d'y compter zéro.
- `weight` est le poids nominal du rôle (moyenné si le joueur a changé de lane sur
  l'échantillon) ; `0` = le rôle ne note pas cette composante (par exemple `Roam`
  pour un jungler).

## `GET /truemains/{nameTag}/rank-history`

Historique de rang (snapshots append-on-change).

**Query** — `days` (int, optionnel). Omis, `0` ou négatif → **90 jours**, pas tout
l'historique ; la valeur est ensuite clampée à [1, 730], sans `400`.

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
- Les fenêtres des séries calendaires sont fixes avant le rabot de la rétention :
  **60 parties**, **30 jours**, **12 semaines**. La couverture réelle peut donc être
  plus courte, jamais plus longue.

## `GET /truemains/{nameTag}/matches`

Historique de matchs paginé.

**Query**

| Param        | Type | Requis | Défaut | Description |
|--------------|------|--------|--------|-------------|
| `page`       | int  | non    | 1      | Page 1-based. `< 1` ramené à 1, sans `400`. |
| `pageSize`   | int  | non    | 20     | 0/omis → 20, plafonné à 50, sans `400`. |
| `position`   | string | non  | toutes | Filtre position. Valeur non reconnue **ignorée**. |
| `championId` | int  | non    | tous   | Filtre champion. `≤ 0` ignoré. |

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
        "teamId": 100, "position": "MIDDLE", "win": true,
        "lpDelta": null,
        "performanceScore": 78, "placement": 2,
        "isMvp": true, "isAce": false
      },
      "participants": [
        { "championId": 103, "teamId": 100, "position": "MIDDLE", "gameName": "Faker", "tagLine": "KR1" },
        { "championId": 157, "teamId": 200, "position": "MIDDLE", "gameName": null, "tagLine": null }
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
- `position` (sur `self` comme sur chaque participant) : position Riot, `null` quand
  Riot n'en a assigné aucune (modes hors Faille, remakes). Renvoyée pour que le front
  badge le rôle sans ré-identifier le joueur par `(équipe, champion)`, ambigu dans les
  queues qui autorisent les champions en double.
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
entrées, même score. **Neuf** composantes sont normalisées sur `0..1` puis moyennées
avec des poids qui dépendent du rôle (`teamPosition`), le résultat étant remis à
l'échelle `0..100` :

| Composante (`kind`) | Normalisation |
| --- | --- |
| `Combat` | `(kills + assists) / max(1, deaths)`, linéaire jusqu'à 6.0 KDA = plein |
| `KillParticipation` | `(kills + assists) / teamKills`, borné à 1 |
| `DamageShare` | part des dégâts aux champions de l'équipe, bande 5 % → 35 % |
| `GoldShare` | part de l'or de l'équipe, bande 10 % → 30 % |
| `Farming` | CS/min vs une référence par rôle (2.0 support … 9.5 bot) |
| `Vision` | vision score/min vs une référence par rôle (0.8 bot … 2.4 support) |
| `Laning` | avances sur les marques de **phase de lane** (≤ 15 min) |
| `MidGame` | mêmes avances sur les marques **post-lane** (> 15 min) |
| `Roam` | participations aux kills prises **hors de sa lane** en début de partie, vs une référence par rôle |

`Laning` et `MidGame` partagent la même construction : chaque marque de timeline
(5, 10, 15… minutes) mixe les avances or 50 % / cs 25 % / xp 25 %, centrées (lane
égale = 0.5). La saturation est **proportionnelle à la minute de la marque** — 100 or,
2 cs et 100 xp par minute écoulée — donc ±500 or / ±10 cs / ±500 xp à la minute 5 et
±1500 / ±30 / ±1500 à la minute 15 : 1 000 or d'avance écrase à 10 minutes et n'est
qu'ordinaire à 30. Les marques d'une phase sont ensuite moyennées **pondérées par leur
propre minute**, pour que les marques tardives d'une phase, plus décisives, pèsent
davantage. `MidGame` tombe quand la partie s'arrête avant la première marque post-lane.

`Roam` tombe pour JUNGLE (sa référence vaut `0` : un jungler n'a pas de lane à
quitter) et quand le match n'a aucune couverture de positions de kill.

Poids par rôle (somme = 100 ; `teamPosition` vide ou inconnu → profil neutre) :

| Rôle | Combat | KP | Dégâts | Or | Farm | Vision | Lane | MidGame | Roam |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| TOP | 20 | 14 | 16 | 7 | 14 | 5 | 12 | 7 | 5 |
| JUNGLE | 18 | 18 | 14 | 7 | 14 | 7 | 12 | 10 | 0 |
| MIDDLE | 20 | 14 | 18 | 7 | 14 | 5 | 10 | 6 | 6 |
| BOTTOM | 20 | 12 | 20 | 7 | 16 | 4 | 10 | 8 | 3 |
| UTILITY | 18 | 20 | 7 | 4 | 5 | 24 | 8 | 6 | 8 |
| neutre | 20 | 16 | 16 | 7 | 12 | 7 | 10 | 8 | 4 |

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

Cockpit opérateur (#1031) : un verdict roulé, une ligne par signal, et les mesures
brutes derrière. Il **compose** les signaux qui ont déjà leur propre panneau (les
détecteurs de qualité, le rollup de runs, la prévision disque) plutôt que de les
re-mesurer, pour qu'une tuile ne puisse jamais contredire la page vers laquelle elle
pointe. Le verdict vit ici et non dans le front, parce qu'un seuil est une décision
métier et qu'un second consommateur (l'alerting) doit obtenir la même réponse.

Pas de paramètre.

**Réponse `200`** — `PipelineHealthReadModel`

```json
{
  "status": "amber",
  "headline": "1 signal is failing",
  "evaluatedAtUtc": "2026-08-05T12:00:00Z",
  "signals": [
    {
      "key": "dataQuality",
      "title": "Data quality",
      "status": "amber",
      "headline": "2 detectors are amber.",
      "unknownReason": null,
      "detailPath": "/data-quality"
    }
  ],
  "processes": [
    {
      "processName": "Discovery", "status": "Success",
      "lastStartedAtUtc": "2026-06-26T10:00:00Z",
      "lastFinishedAtUtc": "2026-06-26T10:02:00Z",
      "lastSuccessAtUtc": "2026-06-26T10:02:00Z",
      "consecutiveFailures": 0,
      "durationMs": 120000, "error": null
    }
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

- `status` ∈ `green` / `amber` / `red` / `unknown` : le **pire** des `signals`, avec la
  précédence `red > amber > unknown > green` — même vocabulaire et même précédence que
  les détecteurs de qualité, pour qu'une pastille veuille dire la même chose partout
  dans le portail.
- `signals[].key` ∈ `processes` / `dataQuality` / `ingestionLag` / `diskForecast`, et
  `detailPath` est la route admin qui porte le détail : chaque tuile est un lien, et ce
  payload ne garde volontairement aucune profondeur.
- `signals[].unknownReason` est renseigné **si et seulement si** le `status` du signal
  est `unknown` — la tuile l'affiche à la place d'un zéro qui aurait l'air sain. Un
  sous-signal dégradé doit s'expliquer, jamais faire échouer la page.
- `evaluatedAtUtc` est affiché : un cockpit qui ne dit pas son âge se fait lire comme
  du live alors qu'il a une minute.
- `processes[].status` est l'état **effectif**, pas celui stocké : un run `Running`
  dont le heartbeat a vieilli se lit `Abandoned` ici, via la même politique que
  `GET /ops/process-runs`.
- `processes[].lastSuccessAtUtc` est `null` quand le process n'a **jamais** réussi —
  une réponse différente de « a réussi il y a longtemps », qui ne doit pas s'y
  confondre. `consecutiveFailures` est la série d'échecs en cours, `0` quand le dernier
  run a réussi.

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
| `granularity` | string | **oui** | `day` / `week` / `month` / `year` / `patch` (insensible à la casse). Manquant/invalide → `400`. |
| `region`      | string | non    | Filtre PlatformId. |

**Réponse `200`** — `MatchTimeBucket[]` (chronologique)

```json
[
  { "bucket": "2026-06-01T00:00:00Z", "matches": 52000 },
  { "bucket": "2026-06-08T00:00:00Z", "matches": 48000 }
]
```

`bucket` = timestamp ISO UTC du début de période tronquée (`day`/`week`/`month`/
`year`), ou `MAJEUR.MINEUR` (`16.4`) pour `patch` — les patchs sont ordonnés sur la
partie la plus ancienne de chacun, donc chronologiquement et non lexicalement.

## `GET /ops/stats/matches-ingested`

Débit d'ingestion (#1025) : combien de matchs le pipeline a réellement **ingérés**
par période, agrégé depuis les summaries des runs `MatchIngestion` en Mongo.

Ce n'est **pas** une variante de `stats/matches-over-time`, qui bucketise sur
`GameStartTimeUtc` — donc *quand les parties ont été jouées*. Cette série-là bouge à
peine quand l'ingestion cale, et grossit *dans le passé* quand un backfill tombe.
Celle-ci répond à « est-ce que le pipeline a suivi cette semaine ».

Source volontairement les summaries de runs et non `matches.CreatedAtUtc` : la
rétention supprime des matchs, donc un bucket ancien rétrécirait avec le temps — une
courbe qui réécrit son propre passé. Supprimer un match ne réécrit pas le run qui l'a
ingéré.

**Query**

| Param        | Type   | Requis | Défaut | Description |
|--------------|--------|--------|--------|-------------|
| `granularity`| string | oui    | —      | `day`, `week` ou `month`. `patch` et `year` sont refusés (400) : un patch est une propriété des parties, pas de leur ingestion, et une année ne peut pas remplir deux buckets sous la rétention des runs. |
| `windowDays` | int    | non    | `30`   | Fenêtre en jours. `0`, négatif ou omis → **30** (traité comme absent, pas ramené à 1) ; sinon clampé à [1, 365]. |

**Réponse `200`** — `MatchesIngestedReadModel`

```json
{
  "buckets": [
    { "bucket": "2026-08-04T00:00:00Z", "matchesInserted": 42, "matchesSkipped": 5, "timelinesUpdated": 18, "runs": 2 }
  ],
  "windowDays": 30,
  "retentionDays": 180,
  "earliestRunAtUtc": "2026-07-07T02:14:00Z"
}
```

- `bucket` : début de période en ISO-8601 UTC, même forme que `matches-over-time`.
  Les semaines commencent le **lundi**, comme `date_trunc('week')` côté Postgres — le
  `$dateTrunc` de Mongo commencerait dimanche et les deux graphes ne s'aligneraient pas.
- `runs` : nombre de runs démarrés dans la période, **summary ou non**. Un run échoué
  ou abandonné n'a pas de compteurs mais reste une tentative : le retirer ferait
  passer un ingestor en crash-loop pour un ingestor au repos.
- `matchesSkipped` / `timelinesUpdated` : portés parce que `matchesInserted` seul ne
  distingue pas « plus rien à faire » de « tourne à fond et n'écrit rien », qui sont
  deux états opposés.
- Les périodes creuses **à l'intérieur** de la plage observée sont présentes à zéro —
  un pipeline arrêté est précisément ce que ce graphe doit montrer. En revanche rien
  n'est rempli **avant** `earliestRunAtUtc` : une période que la rétention a déjà
  effacée n'a pas été mesurée, et la peindre à zéro affirmerait un repos dont on n'a
  aucune trace.
- `retentionDays` : TTL de `process_runs`. Renvoyé pour que le panneau énonce la borne
  au lieu de laisser la queue vide passer pour une absence d'ingestion.

## `GET /ops/stats/aggregations`

Instantané des pipelines d'agrégation pour le panneau Aggregation : par famille les
comptes de lignes **exacts** de ses tables, la couverture champions/patchs, la
fraîcheur des données et le dernier run enregistré ; plus les backlogs d'ingestion qui
doivent lire zéro quand les agrégations ont rattrapé leur retard.

Cinq familles, dans cet ordre, identifiées par leur `key` — valeurs de contrat, le
front branche ses libellés dessus : `builds`, `matchups`, `synergies`, `powerspikes`,
`mains`.

Pas de paramètre.

**Réponse `200`** — `AggregationsReadModel`

```json
{
  "queueId": 420,
  "families": [
    {
      "key": "builds",
      "processName": "ChampionPatternAggregation",
      "tables": [ { "table": "champion_dim_item_paths", "rows": 4200000 } ],
      "totalRows": 9800000,
      "distinctChampions": 168,
      "distinctPatches": 3,
      "lastAggregatedAtUtc": "2026-08-05T09:12:00Z",
      "lastRun": {
        "status": "Success",
        "lastStartedAtUtc": "2026-08-05T09:00:00Z",
        "lastFinishedAtUtc": "2026-08-05T09:12:00Z",
        "lastSuccessAtUtc": "2026-08-05T09:00:00Z",
        "durationMs": 720000,
        "lastSuccessSummary": { "championsFolded": 168 }
      }
    }
  ],
  "backlog": {
    "pendingPowerspikeMatches": 0,
    "pendingSynergyMatches": 12400,
    "pendingEloBracketParticipants": 0,
    "timelineIngestedMatches": 980000
  }
}
```

- `lastAggregatedAtUtc` est l'écriture de ligne d'agrégat la plus récente : une
  fraîcheur **de la donnée**, indépendante des enregistrements de run. `lastRun` est
  `null` quand le process n'a jamais tourné, et son `lastSuccessAtUtc` diffère de
  `lastFinishedAtUtc` quand le dernier run a échoué.
- `distinctPatches` est `null` pour une famille sans axe patch.
- `pendingSynergyMatches` démarre au **nombre total de matchs retenus** : le drapeau
  de repli est livré à `false` sur toutes les lignes préexistantes, donc ce compteur
  affiche un gros backlog au premier déploiement puis se vide au fil des runs (#922).
- `timelineIngestedMatches` n'est **pas** un backlog, et n'est le dénominateur que de
  `pendingPowerspikeMatches` : les deux se filtrent sur `TimelineIngested`. Les deux
  autres compteurs ont chacun le leur — `pendingSynergyMatches` compte tous les matchs
  de la queue, timeline ou non, et `pendingEloBracketParticipants` compte des
  **participants**, une autre unité. Les rapprocher au même dénominateur donnerait des
  pourcentages faux.

## `GET /ops/patch-coverage`

Est-ce que les patchs que lisent les surfaces publiques sont réellement servables
(#1033) : par patch, les matchs et participants ingérés par date de partie, combien
de lignes `(champion, voie)` ont un agrégat et combien franchissent le plancher de
games que lit l'annuaire des champions, lesquelles restent en dessous, et la
couverture + fraîcheur de chaque repli sur ce patch.

Endpoint séparé (et non une carte du panneau Aggregation) parce que ce sont des
scans groupés sur des tables sans index sur leur colonne de patch : abordable
derrière une navigation explicite, pas sur une page qui se charge à la connexion.

Pas de paramètre.

**Réponse `200`** — `PatchCoverageReadModel`

```json
{
  "queueId": 420,
  "minSampleGames": 10,
  "floorNote": "A (champion, lane) line needs 10 games before the public reads it.",
  "currentPatch": "16.4",
  "verdict": "servable",
  "status": "green",
  "headline": "16.4 has 412 lines past the 30-game floor.",
  "unknownReason": null,
  "patches": [
    {
      "patch": "16.4",
      "isCurrent": true,
      "verdict": "servable",
      "status": "green",
      "headline": "412 lines past the floor, judged against a bar of 380.",
      "matches": 52000, "participants": 520000,
      "firstGameStartUtc": "2026-08-01T00:12:00Z",
      "lastGameStartUtc": "2026-08-05T11:40:00Z",
      "daily": [ { "date": "2026-08-01", "matches": 9800, "participants": 98000 } ],
      "lines": 640, "linesPastFloor": 412,
      "champions": 168, "championsPastFloor": 151,
      "servableLinesBar": 380.0,
      "servableLinesBarNote": "Median of the 4 comparable patches.",
      "belowFloorCount": 228,
      "belowFloor": [ { "championId": 266, "position": "TOP", "games": 27, "gamesToFloor": 3 } ],
      "folds": [
        {
          "key": "bans", "label": "Bans", "measured": true,
          "firstMeasuredPatch": "16.1", "notMeasuredNote": null,
          "rows": 168, "champions": 168,
          "lastAggregatedAtUtc": "2026-08-05T09:00:00Z", "ageHours": 2.6,
          "status": "green", "pendingMatches": 0,
          "note": "Feeds the directory's banRate and the tier-list ban term."
        }
      ]
    }
  ],
  "sourceNote": "Grouped scans over matches, champion_aggregate_scopes and the fold tables.",
  "evaluatedAtUtc": "2026-08-05T12:00:00Z"
}
```

- `verdict` est résolu **premier-match-gagne** : `servable` | `notAggregated`
  (des matchs sont ingérés mais aucune ligne d'agrégat n'existe encore — *attendre,
  ou vérifier le repli*) | `thin` (agrégé, et toujours sous la barre — *le patch
  manque réellement de parties*) | `unknown` (rien d'ingéré et rien d'agrégé, donc
  aucune lecture à donner). `notAggregated` et `thin` sont séparés exprès : ce sont
  les deux causes du même chiffre bas, et elles appellent des réactions opposées.
- `unknownReason` n'est renseigné que lorsque `verdict` vaut `unknown` faute d'une
  mesure : sans le rollup de couverture, « mince » et « pas agrégé » sont
  indiscernables, et deviner entre les deux serait pire que se taire.
- `minSampleGames` est **repris** de `ChampionsList:MinSampleGames`, pas redéclaré :
  la page ne peut donc jamais juger contre une barre que le site n'applique pas.
- `belowFloor` est trié **au plus près du plancher d'abord** (la question posée par
  un patch mince est « il s'en faut de combien »), plafonné par
  `PatchCoverage:ThinLineLimit` ; `belowFloorCount` porte le total réel.
- `folds[].measured` à `false` = ce patch **précède entièrement** le repli. Les
  payloads bruts ne sont pas conservés, donc un repli arrivé en cours de corpus ne
  peut pas être rattrapé (#920 bans, #957 powerspikes par adversaire) : ses lignes
  sur les patchs antérieurs sont absentes **par construction**, pas manquantes. Tous
  les compteurs de la ligne valent alors `null` et `notMeasuredNote` le dit — un zéro
  se lirait « le repli est cassé », la seule chose qu'il n'est pas.
- `folds[].pendingMatches` est `null` pour les replis sans drapeau par match (builds
  et mains remplacent par scope et par compte), où un backlog ne s'exprime pas en
  nombre de matchs.
- Tous les âges sont relatifs à `evaluatedAtUtc`, pas à l'horloge du navigateur.

## `GET /ops/db/tables`

Empreinte de stockage des tables Postgres **et des collections Mongo** (#1023) — les
deux moteurs partagent le même volume, donc la liste ne s'arrête pas à la frontière
d'un moteur. Triée par `totalBytes` décroissant, tous moteurs confondus.

**Réponse `200`** — `TableStatRow[]`

```json
[
  { "engine": "postgres", "tableName": "match_participants", "rowEstimate": 15000000, "totalBytes": 8589934592, "tableBytes": 5368709120, "indexBytes": 3221225472 },
  { "engine": "mongo", "tableName": "logs", "rowEstimate": 4200000, "totalBytes": 3120508928, "tableBytes": 2684354560, "indexBytes": 436154368 }
]
```

- `engine` : `postgres` ou `mongo`. Nécessaire et pas cosmétique — `process_runs` et
  `seed_requests` existent des deux côtés (table Postgres gelée + collection Mongo).
- `rowEstimate` : estimation du planner côté Postgres, **compte exact** côté Mongo.
- Les collections Mongo sont absentes de la réponse si Mongo n'est pas configuré : le
  moteur n'est alors pas mesuré, ce qui n'est pas la même chose que vide.

## `GET /ops/db/history`

Croissance du stockage + prévision de saturation disque (#925). Lit uniquement les
snapshots quotidiens (collection Mongo `db_table_size_snapshots`) — aucun scan
`pg_catalog` à la volée, contrairement à `db/tables`.

**Query**

| Param        | Type | Requis | Défaut                        | Description |
|--------------|------|--------|-------------------------------|-------------|
| `windowDays` | int  | non    | `StorageHistory:DefaultWindowDays` (90) | Fenêtre d'historique en jours. `0`, négatif ou omis → le défaut configuré ; sinon clampé à [1, 730], sans `400`. |

**Réponse `200`** — `DbStorageHistoryReadModel` (toujours `200`, tout vide tant que le
process de snapshot n'a pas tourné)

```json
{
  "daily": [
    { "dateUtc": "2026-07-27T00:00:00Z", "databaseBytes": 44352195072, "postgresBytes": 41231686144, "mongoBytes": 3120508928, "totalBytes": 39000000000, "rowEstimate": 21000000 }
  ],
  "engines": ["mongo", "postgres"],
  "comparableDays": 90,
  "tables": [
    {
      "engine": "postgres",
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

- `databaseBytes` : `pg_database_size` **mesuré** (catalogues compris) **+** la taille
  sur disque de Mongo (`dbStats.storageSize + indexSize`), **sommés** — les deux moteurs
  sont sur le même volume, donc c'est bien la somme qui le remplit, jamais le plus gros
  des deux. C'est ce que la prévision extrapole. `postgresBytes` / `mongoBytes` donnent
  la répartition. `totalBytes` n'est que la somme des objets, donc toujours plus petit.
- `engines` : les moteurs réellement mesurés sur la fenêtre. Avant le premier snapshot
  Mongo, et partout où Mongo n'est pas configuré, les totaux ne couvrent que Postgres —
  le panneau l'affiche au lieu de les présenter comme « le disque ».
- `rowEstimate` : somme des estimations du planner, indicateur de tendance et non un
  compte exact.
- `tables` : uniquement les plus grosses (`StorageHistory:TopTables`, 10 par défaut) ;
  les autres restent comptées dans `daily`.
- `growthRate` : `null` si la table était vide en début de fenêtre (croissance non
  définie plutôt qu'infinie).
- `forecast` : **`null`** s'il y a moins de 3 jours d'historique, si le stockage est
  stable ou décroissant, ou si `StorageHistory:DiskCapacityBytes` n'est pas configuré.
  Aucune valeur de remplacement n'est inventée. « Jours d'historique » ne compte que les
  jours mesurant les **mêmes moteurs** que le plus récent : le jour où Mongo commence à
  être mesuré ajoute son empreinte d'un coup, et ajuster une tendance à travers cette
  marche lirait un saut unique comme un rythme quotidien. `comparableDays` expose ce
  compte, pour que le panneau nomme la vraie raison de l'absence au lieu de redériver
  la règle de son côté.
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

| Param          | Type     | Requis | Description |
|----------------|----------|--------|-------------|
| `level`        | string   | non | Seuil de sévérité **minimum** (`Warning`, `Error`…), pas un filtre exact. |
| `category`     | string   | non | Préfixe de catégorie de logger, insensible à la casse. |
| `since`        | datetime | non | Borne basse temporelle. |
| `search`       | string   | non | Substring insensible à la casse sur message / exception. |
| `eventType`    | string   | non | Nom d'ops-event exact, insensible à la casse (ex. `CandidateValidated`). |
| `process`      | string   | non | Hôte producteur : `Api` / `Ingestor` (exact, insensible à la casse). |
| `hasException` | bool     | non | `true` → uniquement les lignes portant une exception formatée. |
| `page`         | int      | non | 1-based. |
| `pageSize`     | int      | non | Taille de page. Omis/≤0 → 50, clampé à [1, 200]. |

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
  "eventTypes": ["CandidateValidated", "SeedRequested", "MatchIngested"],
  "processes": ["Api", "Ingestor"]
}
```

`eventTypes` et `processes` sont des catalogues **statiques** accompagnant chaque
réponse pour peupler les filtres — ils ne se déduisent pas des `entries` de la page.

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
| `pageSize` | int      | non | Taille de page. Omis/≤0 → 25, clampé à [1, 100] — pas le même défaut que `/ops/logs`. |

**Réponse `200`** — `CrashesReadModel`

```json
{
  "entries": [
    {
      "id": "665fd2a1c3b4e5f6a7b8c9d0",
      "timestampUtc": "2026-06-26T10:01:23Z",
      "processName": "Ingestor",
      "source": "AppDomainUnhandled",
      "explanation": "The process ran out of memory during champion pattern aggregation.",
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
  "pageSize": 25,
  "sources": ["AppDomainUnhandled", "TaskSchedulerUnobserved", "HostRun", "UncleanShutdown"],
  "processes": ["Api", "Ingestor"]
}
```

Pour un `UncleanShutdown`, les champs d'exception sont `null` et les champs mémoire
portent le dernier snapshot connu du run mort (le signal OOM).

`explanation` est une lecture en langage clair du crash (#722), dérivée de la source,
de la chaîne d'exceptions et — pour un arrêt sale — du snapshot mémoire et du code de
sortie. C'est du **texte d'affichage heuristique** ; les champs bruts au-dessus restent
la référence. Toujours présent, jamais `null`.

## `GET /ops/riot-usage`

Métriques d'usage de la Riot API sur une fenêtre relative.

**Query**

| Param      | Type   | Requis | Description |
|------------|--------|--------|-------------|
| `window`   | string | non | `1h` / `24h` (défaut) / `7d`. Valeur vide ou inconnue → repli silencieux sur `24h`, jamais un `400` ; la réponse **réénonce** la fenêtre appliquée dans `window`, donc l'appelant peut toujours voir ce qu'il a réellement obtenu. |
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
    {
      "endpoint": "match-v5.match", "calls": 300000, "successes": 299100, "errors": 900,
      "avgLatencyMs": 150.2, "lastCalledAtUtc": "2026-06-26T09:59:50Z",
      "methodRateLimit": "2000:10", "methodRateLimitCount": "150:10"
    }
  ],
  "statusCodes": [ { "statusCode": 200, "count": 478800 }, { "statusCode": 429, "count": 800 }, { "statusCode": 0, "count": 50 } ],
  "timeSeries": [ { "bucketUtc": "2026-06-26T09:00:00Z", "calls": 20000, "errors": 40, "retries": 120 } ],
  "rateLimit": {
    "observedAtUtc": "2026-06-26T09:59:50Z",
    "appRateLimit": "20:1,100:120",
    "appRateLimitCount": "3:1,57:120",
    "methodRateLimit": "2000:10",
    "methodRateLimitCount": "150:10",
    "retryAfterSeconds": null,
    "rateLimitType": null
  },
  "callerBreakdown": [
    { "caller": "MatchIngestion", "calls": 410000, "errors": 1000 }
  ],
  "headroom": {
    "sufficientData": true,
    "observedWindowHours": 168.0,
    "requiredWindowHours": 24.0,
    "trackedAccounts": 10400,
    "callsPerAccountPerDay": 42.5,
    "observedCallsPerDay": 442000.0,
    "bindingLimit": { "limit": 100, "windowSeconds": 120, "maxCallsPerDay": 72000.0 },
    "spareCallsPerDay": 18000.0,
    "additionalAccountsHeadroom": 423
  }
}
```

- `statusCodes[].statusCode == 0` = faute transport (pas de réponse).
- `rateLimit` est `null` si aucun en-tête observé dans la fenêtre. Les chaînes
  `appRateLimit*` / `methodRateLimit*` sont les en-têtes Riot **verbatim**
  (`20:1,100:120`), au front de les parser.
- `callerBreakdown` attribue les appels à chaque process appelant, `calls` décroissant
  (#1035) : « qui consomme le budget » n'est pas la même question que « quel endpoint ».
- `headroom` (#1035) estime « combien de comptes suivis en plus tiendraient », et est
  **toujours calculé sur 7 jours**, indépendamment de `window` — un budget ne se juge
  pas sur une heure. `sufficientData` à `false` (avec seulement `observedWindowHours` /
  `requiredWindowHours` renseignés, le reste `null`) quand l'historique est trop mince,
  qu'aucun compte n'est suivi ou qu'aucun snapshot de rate-limit n'a été vu : l'estimation
  rend cet état absent au lieu d'extrapoler d'une fenêtre trop courte.
- `headroom.bindingLimit` est la fenêtre de rate-limit applicatif dont le plafond
  journalier soutenu est le **plus petit** — celle qui contraint en premier sous charge
  continue, pas celle dont le ratio instantané est le plus haut.

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
        { "label": "duplicate groups", "amber": 1, "red": 1, "unit": "count", "direction": "above" }
      ],
      "hasDrillDownEndpoint": false
    }
  ],
  "evaluatedAtUtc": "2026-07-30T18:00:00Z"
}
```

- `thresholds[].direction` ∈ `above` (défaut) / `below` : sans lui les nombres `amber`
  et `red` sont illisibles pour les détecteurs qui alertent **en dessous** d'un seuil
  (une couverture qui tombe), où `amber: 1` voudrait dire l'inverse de ce qu'on croit.
- `count` est le chiffre-titre du détecteur ; il est `null` **exactement** quand
  `status` vaut `unknown` — cohérent avec la règle ci-dessus : rien n'a été mesuré,
  donc il n'y a pas de chiffre, et surtout pas un `0` qui aurait l'air sain.

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
| `issue`       | string | non | Un check : `missingTimeline`, `wrongParticipantCount`, `missingTeamPosition`, `zeroDuration`, `duplicateChampion`. Valeur inconnue **ignorée** (aucun filtre, `200`), pas un `400`. |
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
  "staleTimelineThresholdHours": 6
}
```

`staleTimelineThresholdHours` est une **constante serveur** (6), pas de la
configuration : elle est réénoncée dans la réponse pour que le panneau affiche le seuil
qu'il vient réellement d'appliquer plutôt qu'un nombre codé en dur de son côté.

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

## `GET /ops/configuration`

Ce que chaque hôte fait réellement tourner (#1034) : les options de l'Api lues
**en direct** dans son conteneur, plus celles de l'Ingestor — publiées en Mongo à
son propre démarrage, puisque l'Api ne peut pas introspecter un process dans lequel
elle ne tourne pas. Lecture seule. Aucune section porteuse de secret n'est jamais
incluse (allow-list `Data.Configuration.EffectiveConfigurationCatalog`).

Pas de paramètre.

**Réponse `200`** — `EffectiveConfigurationOverviewReadModel`

```json
{
  "processes": [
    {
      "processName": "Api",
      "environment": "Production",
      "version": "1.6.0",
      "capturedAtUtc": "2026-08-05T12:00:00Z",
      "sections": [
        {
          "name": "StorageHistory",
          "title": "Storage history",
          "description": "Drives the db/history window and the disk forecast.",
          "values": [
            {
              "key": "StorageHistory:DiskCapacityBytes",
              "name": "DiskCapacityBytes",
              "value": "107374182400",
              "valueLabel": "100 GB",
              "origin": "override",
              "source": "environment",
              "unit": "bytes",
              "notice": null
            }
          ]
        }
      ]
    }
  ]
}
```

- `processes` est trié par nom de process (`Api`, `Ingestor`).
- `capturedAtUtc` vaut « maintenant » pour l'Api (construit à chaque requête) ; pour
  l'Ingestor c'est **l'heure de boot de son dernier run** — toujours ce que ce
  process fait tourner, même si c'est antérieur au dernier déploiement.
- `origin` ∈ `default` / `override` / `derived`. `source` nomme le provider d'un
  override (ex. `environment`) et est `null` pour `default`/`derived`.
- `value` est la forme recollable dans la configuration, `null` quand l'option n'est
  pas définie ; `valueLabel` est la forme humaine (« 90 days », « 1.0 TB »), `null`
  quand elle ne ferait que répéter `value`.
- `notice` est renseigné quand une option non définie a une conséquence visible
  ailleurs dans le portail.

## `POST /ops/accounts/freshness`

Pendant **par lot** de `GET /ops/accounts/{nameTag}` : pour chaque Riot ID,
est-ce qu'on le suit déjà, est-il encore exploitable, et quand l'a-t-on ingéré pour
la dernière fois. Rien d'autre.

Un `POST` parce que l'entrée est une liste, pas parce que ça écrit — c'est une
**lecture**. Il existe pour qu'un appelant par lot cesse de boucler sur l'explorateur
de comptes : celui-ci trace un Riot ID à travers tout le pipeline, ce qui est juste
pour un opérateur et ruineux quelques milliers de fois d'affilée (le premier run du
seeder OTP l'a poussé à des timeouts de 30 s sur le site en production).

**Body** — `AccountFreshnessRequest`, plafonné à **1000** entrées par requête
(au-delà : `400` ; un appelant qui en a plus envoie plusieurs lots).

```json
{
  "accounts": [
    { "gameName": "Faker", "tagLine": "KR1", "platformId": "KR" }
  ]
}
```

**Réponse `200`** — `AccountFreshnessResponse` · **`400`** pour une entrée sans
`gameName`/`platformId`, une route de plateforme inconnue, ou un lot au-dessus de la
limite. Une liste vide (ou absente) est un `200` avec `accounts: []`.

```json
{
  "accounts": [
    {
      "gameName": "Faker",
      "tagLine": "KR1",
      "platformId": "KR",
      "known": true,
      "status": "Active",
      "lastMatchIngestAtUtc": "2026-08-05T03:20:00Z"
    }
  ]
}
```

- Le Riot ID est renvoyé **tel que l'appelant l'a écrit**, pour qu'une réponse puisse
  être rappariée à sa demande.
- `status` ∈ `Active` / `Invalid` (account-v1 a déjà 404 sur le puuid stocké et on
  l'a enregistré). `null` quand `known` est `false`.
- `lastMatchIngestAtUtc` est `null` **dans deux cas** : compte inconnu, et compte
  suivi dont le tour n'est jamais venu. L'appelant les sépare avec `known` — et c'est
  le second qui mérite une action : le compte est dans la population et n'a pourtant
  jamais été récupéré.

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

**Path** — `id` (GUID). La contrainte de route est `{id:guid}` : un id malformé ne
matche aucune route et donne un **`404`**, pas un `400` — même chose sur
`GET /ops/candidates/{id}`.

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

File des requêtes de seed, récentes d'abord, **paginée** côté serveur.

> Paginée plutôt que plafonnée à une liste « récente » : le seeder OTP hebdomadaire
> pousse des dizaines de milliers de requêtes dans cette file en un seul run, donc une
> liste bornée à ses 200 dernières lignes n'en montrait qu'un résidu et ne pouvait pas
> dire combien il reste à drainer. Le `total` de la réponse répond exactement à ça.

**Query**

| Param      | Type   | Requis | Description |
|------------|--------|--------|-------------|
| `status`   | string | non    | Un `SeedRequestStatus` (`Pending`/`Resolving`/`Ingested`/`Failed`), insensible à la casse. Une valeur inconnue est **ignorée** (aucun filtre). |
| `search`   | string | non    | Substring insensible à la casse sur `gameName` / `tagLine`. |
| `region`   | string | non    | PlatformId (ex. `EUW1`). Contrairement à `status`, une valeur non parsable renvoie **400** : le filtre est un match exact, donc une faute de frappe renverrait une page vide, ce qui se lit « aucune requête dans cette région » au lieu de « ce n'est pas une région ». |
| `page`     | int    | non    | 1-based. |
| `pageSize` | int    | non    | Clamp [1, 100], défaut 25. |

**Réponse `200`** — `SeedRequestsReadModel`

```json
{
  "requests": [ /* SeedRequestReadModel[], même forme que ci-dessus */ ],
  "total": 11565,
  "page": 1,
  "pageSize": 25
}
```

`total` compte toutes les lignes correspondant aux filtres, **avant** pagination.
`page` et `pageSize` sont les valeurs *clampées* effectivement appliquées, pas celles
demandées.

## `GET /ops/accounts/{nameTag}`

Explorateur de comptes (#1032) : trace **un** Riot ID à travers tout le pipeline —
identité et fraîcheur par process, bail d'ingestion des matchs, funnel de candidats,
champions mains analysés, historique de rang — en une seule lecture. Il répond à
« pourquoi ce joueur n'apparaît-il pas sur le site ? ».

Lecture seule et **base de données uniquement** : l'Api n'embarque pas de client
Riot, donc cet endpoint ne résout jamais un Riot ID que le pipeline n'a pas déjà
enregistré, et ne sait pas distinguer « jamais découvert » de « ce Riot ID n'existe
pas chez Riot ».

> Déclaré **après** les routes littérales `accounts/seed` : un segment littéral prime
> sur le paramètre `{nameTag}`, donc `/ops/accounts/seed` continue de résoudre vers
> les endpoints de seed. Le pendant **par lot** de cet endpoint est
> `POST /ops/accounts/freshness`, à utiliser dès qu'on interroge plus d'un compte.

**Path** — `nameTag`, soit tel qu'écrit (`Name#TAG`, percent-encodé), soit sous la
forme slug à tiret des routes publiques (`Name-TAG`).

**Query**

| Param    | Type   | Requis | Description |
|----------|--------|--------|-------------|
| `region` | string | non    | Restreint à une plateforme (ex. `EUW1`). Omis, la recherche couvre toutes les régions — un Riot ID n'est unique qu'à l'intérieur d'une région de routage, et les homonymes sont alors listés dans `otherAccountsWithSameRiotId`. |

**Réponse `200`** — `AccountExplorerReadModel` · **`400`** si `nameTag` ne parse ni
en `Name#TAG` ni en `Name-TAG`, ou si `region` n'est pas une route de plateforme
connue (une faute de frappe silencieusement traitée comme « jamais découvert » serait
un mensonge).

**Pas de `404`** : un Riot ID inconnu est un `200` avec `state: "NeverDiscovered"`.
« On n'a jamais vu ce compte » est une réponse, pas une erreur — un `404` s'afficherait
dans l'admin comme une panne plutôt que comme un verdict.

```json
{
  "query": { "gameName": "Faker", "tagLine": "KR1", "region": "KR" },
  "state": "Tracked",
  "stateDetail": "In the match-ingestion population as an established main.",
  "identity": {
    "riotAccountId": "11112222-3333-4444-5555-666677778888",
    "puuid": "abcdef0123456789…",
    "gameName": "Faker", "tagLine": "KR1", "platformId": "KR",
    "profileIconId": 6, "summonerLevel": 780, "status": "Active",
    "createdAtUtc": "2026-01-04T09:00:00Z", "updatedAtUtc": "2026-08-05T03:20:00Z",
    "lastProfileSyncAtUtc": "2026-08-05T02:00:00Z",
    "lastRankSyncAtUtc": "2026-08-05T02:00:00Z",
    "lastMainCalcAtUtc": "2026-08-04T23:10:00Z",
    "lastActivityCheckAtUtc": "2026-08-03T18:00:00Z",
    "lastMatchIngestAtUtc": "2026-08-05T03:20:00Z",
    "rankScore": 2840
  },
  "otherAccountsWithSameRiotId": [
    {
      "riotAccountId": "aaaabbbb-cccc-dddd-eeee-ffff00001111",
      "puuid": "0123456789abcdef…", "platformId": "EUW1", "status": "Invalid",
      "lastMatchIngestAtUtc": null
    }
  ],
  "tracking": {
    "isTracked": true, "trackedVia": "EstablishedMain",
    "hasActiveMain": true, "hasQueuedCandidate": false,
    "matchIngestStatus": "Idle", "matchIngestClaimedAtUtc": null,
    "claimAgeSeconds": null,
    "lastMatchIngestAtUtc": "2026-08-05T03:20:00Z",
    "neverIngested": false
  },
  "matchesIngested": {
    "liveParticipantCount": 412,
    "oldestRetainedGameStartUtc": "2026-06-18T10:00:00Z",
    "newestRetainedGameStartUtc": "2026-08-05T01:40:00Z",
    "careerGamesFromAggregates": 1980,
    "aggregatedPatchCount": 14,
    "oldestAggregatedGameStartUtc": "2025-11-02T12:00:00Z",
    "lastAnalysisSampleSize": 50,
    "pruned": true,
    "prunedNote": "Frozen aggregates hold games the live participant rows no longer do."
  },
  "candidates": [
    {
      "id": "aaaa1111-2222-3333-4444-555566667777",
      "championId": 103, "status": "Validated", "source": "Ladder", "score": 92.4,
      "scoreInputs": {
        "lastPlayTimeUtc": "2026-08-04T21:00:00Z",
        "championRankInMasteryTop": 1, "championPoints": 850000,
        "observedGames": 0, "observedWins": 0
      },
      "discoveredAtUtc": "2026-06-20T08:00:00Z",
      "scoredAtUtc": "2026-06-20T08:05:00Z",
      "validatedAtUtc": "2026-06-20T08:30:00Z"
    }
  ],
  "seedRequest": null,
  "mains": {
    "rows": [
      {
        "championId": 103, "totalMatches": 50, "championMatches": 31,
        "playRate": 0.62, "isMain": true, "isOtp": false,
        "isExtendedSample": false, "isActive": true,
        "primaryPosition": "MIDDLE",
        "positionBreakdown": [ { "position": "MIDDLE", "games": 29, "rate": 0.935 } ],
        "calculatedAtUtc": "2026-08-04T23:10:00Z",
        "analysisSkipped": false,
        "deactivation": null
      }
    ],
    "thresholds": {
      "playRateThreshold": 0.20, "playRateFloor": 0.12,
      "otpPlayRateThreshold": 0.85, "minMatchesToEvaluate": 20,
      "effectiveThresholdNote": "The effective per-champion threshold interpolates between the floor and the base threshold…"
    }
  },
  "rankSnapshots": [
    {
      "capturedAtUtc": "2026-08-05T02:00:00Z",
      "tier": "CHALLENGER", "division": "I", "leaguePoints": 1240,
      "wins": 310, "losses": 240
    }
  ]
}
```

- `state` est un verdict en un mot, résolu **premier match gagnant** sur l'échelle
  `NeverDiscovered` → `SeedRequestedOnly` → `Invalidated` → `Tracked` → `Retired` →
  `NotAMain` → `CandidateOnly` → `Discovered`, donc un compte tombe toujours dans
  exactement un état. `stateDetail` en est la phrase, construite côté serveur pour
  que tous les consommateurs expliquent un état identiquement. Les booléens derrière
  restent exposés dans les sections : le state est un titre, pas la source de vérité.
- `identity`, `tracking` et `matchesIngested` sont `null` quand aucune ligne
  `riot_accounts` ne correspond. `candidates` est alors **toujours vide** : un
  candidat est clé sur `(platformId, puuid)` et ne porte pas de Riot ID, donc il est
  inatteignable depuis une recherche par Riot ID.
- `otherAccountsWithSameRiotId` est vide dans le cas normal. `(gameName, tagLine,
  platformId)` n'est délibérément pas unique — les Riot ID sont recyclables et se
  collisionnent entre régions : le résolveur prend la ligne la plus récemment active
  et **liste** les autres au lieu d'arbitrer en silence.
- `tracking.isTracked` est **dérivé**, exactement comme le fait la requête de claim
  d'ingestion : il n'existe pas de colonne. `trackedVia` ∈ `EstablishedMain` /
  `QueuedCandidate` / `Both`, `null` quand aucun des deux bras n'est satisfait.
  `claimAgeSeconds` est `null` tant que le statut est `Idle` ; aucun seuil n'est
  appliqué ici (le bail est de la configuration Ingestor que l'Api ne voit pas),
  donc la réponse **ne dit jamais** qu'un bail est « périmé ».
- `neverIngested` distingue « jamais servi » de « ingéré il y a longtemps » : les
  comptes jamais ingérés passent en premier dans la file de claim, donc ce drapeau
  sur un compte suivi signifie que la file n'a pas encore drainé jusque-là.
- `matchesIngested` porte **trois** comptages non interchangeables, chacun avec la
  population qu'il compte (#927) : `liveParticipantCount` est borné par la rétention,
  `careerGamesFromAggregates` survit à la rétention mais ne couvre que les champions
  mains, `lastAnalysisSampleSize` est plafonné par `MainAnalysis:MatchesToConsider`
  (50) — un plafond, pas un total. `pruned` à `false` **ne prouve pas** qu'aucun
  match n'a été supprimé : les parties hors-main disparaissent sans laisser de trace
  détectable ; `prunedNote` l'explique dans les deux sens.
- `candidates[].source` lit `Ladder` même pour un compte seedé manuellement — le
  process de seed manuel réutilise l'upsert ladder et `ManualSeed` n'est jamais
  assigné en production. `seedRequest` est la seule trace manuelle fiable ; il est
  apparié sur le PUUID + plateforme quand le compte existe, et sur le **texte** du
  Riot ID sinon, ce qui est ce qui rend visible un seed qui n'a jamais résolu.
- `candidates[].scoreInputs` ne donne que les entrées persistées : les composantes du
  score ne sont pas stockées, seul le blend final l'est. Les recalculer ici
  mélangerait l'instantané de rareté d'aujourd'hui à un score produit contre un plus
  ancien, et divergerait silencieusement de `score`.
- `mains.thresholds` accompagne les lignes de la règle qui les a jugées. Seule une
  **bande** est donnée (`playRateFloor` → `playRateThreshold`) : le seuil effectif
  par champion interpole selon un instantané de couverture calculé dans l'Ingestor et
  jamais persisté (#407), donc annoncer un nombre exact serait une invention.
  `effectiveThresholdNote` le dit en toutes lettres.
- `rows[].analysisSkipped` est `true` quand le dernier run de `MainAnalysis` sur le
  compte est plus récent que le `calculatedAtUtc` de la ligne : le process a regardé
  le compte et a refusé d'écraser — le garde-fou échantillon-mince (#825). Ce n'est
  pas un bug de fraîcheur.
- `deactivation` est `null` tant que la ligne est active. `reasonKnown` vaut
  **toujours** `false` : il n'existe pas de colonne de motif de retraite, le process
  n'écrit que le booléen ; `reasonNote` énumère les deux causes qu'il confond.
  `confirmedByActivityCheckAtUtc` est `null` quand la retraite n'a jamais été
  confirmée par un check abouti — un check en échec laisse le drapeau *et* l'horodatage
  intacts.
- `rankSnapshots` : au plus une ligne par jour UTC, solo queue uniquement, les 50 plus
  récentes, jamais élaguées par la rétention — c'est la seule série dont les trous
  sont vraiment des trous de jeu et non de stockage. `wins`/`losses` sont `null` sur
  les snapshots pris avant leur enregistrement. `identity.rankScore` est `null` quand
  le compte n'a jamais été vu classé — pas `0`.

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
      "score": 92.4,
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

`gameName`/`tagLine` `null` tant que le compte n'est pas résolu. `score` est le blend
pondéré du scorer sur une échelle **0–100** (pas 0–1) ; il vaut `0` tant que le
candidat n'est pas passé au statut `Scored`.

## `GET /ops/candidates/funnel`

Débit du funnel de candidats par période : entrées (ladder / harvest / seed
manuel), scorés, promus, validés et rétrogradés (#1024).

Complément de `GET /ops/candidates`, qui donne l'état *instantané* du funnel — un
funnel plein mais complètement bloqué et un funnel qui coule y sont identiques.

La source est le résumé des runs enregistrés dans `process_runs`, **jamais** un
comptage de lignes de `main_candidates` : la rétention supprime les candidats
périmés, donc compter les lignes par statut sur une période passée sous-estime
chaque bucket, et d'autant plus qu'on remonte loin. Un résumé de run est écrit une
fois et jamais réécrit : supprimer le candidat ne décompte pas le run qui l'a
découvert. La série est donc bornée par le TTL de `process_runs`.

**Query**

| Param | Type | Requis | Défaut | Description |
|-------|------|--------|--------|-------------|
| `granularity` | string | oui | — | `day`, `week` ou `month`. Toute autre valeur → `400`. |
| `windowDays` | int | non | 30 | Fenêtre en jours. `0`, négatif ou omis → **30** ; sinon clampée à [1, 365]. |

**Réponse `200`** — `CandidateFunnelReadModel`

```json
{
  "buckets": [
    {
      "bucket": "2026-08-04T00:00:00Z",
      "intakeLadder": 42,
      "intakeHarvest": 7,
      "intakeManual": 2,
      "scored": 140,
      "promoted": 30,
      "validated": 25,
      "demoted": 3,
      "runs": 6
    }
  ],
  "windowDays": 30,
  "retentionDays": 180,
  "earliestRunAtUtc": "2026-07-06T02:14:00Z",
  "validatedFirstMeasuredAtUtc": "2026-08-04T05:00:00Z"
}
```

- `intakeManual` compte des candidats *mis en file*, pas insérés : un seed manuel
  promeut des lignes que la discovery a souvent déjà créées.
- `promoted` est le top-N par plateforme ; l'écart avec `scored` est la coupe
  compétitive, pas une panne.
- `validated` est `null` — et non `0` — pour les périodes antérieures à
  `validatedFirstMeasuredAtUtc` : le compteur n'existait pas encore, et un panneau
  de santé ne présente pas ce qu'il n'a pas mesuré comme un zéro mesuré (#924).
- `demoted` est aujourd'hui la seule sortie négative du funnel : le statut
  `Rejected` existe sur l'entité mais aucun process ne l'assigne.
- `runs` compte les runs des six process contributeurs ; `0` distingue « le
  pipeline n'a pas tourné » de « il a tourné et n'a rien bougé ».
- Les périodes creuses *à l'intérieur* de la plage observée valent zéro ; rien n'est
  rempli avant `earliestRunAtUtc`.
- `retentionDays` (le TTL de `process_runs`) est **rapporté, pas appliqué** :
  `windowDays` n'est clampé qu'à [1, 365], donc demander 365 jours avec un TTL de 180
  renvoie bien `windowDays: 365` — la série s'arrête simplement à `earliestRunAtUtc`.
  C'est ce couple que le panneau affiche pour expliquer une queue vide.

## `GET /ops/candidates/queue-latency`

Latence de file des candidats actuellement retenus : médiane et p90 de
découverte → scoring, puis scoring → validation (#1024).

**Instantané sur les lignes présentes, pas une série historique** — d'où l'absence
de fenêtre. Les candidats élagués n'y sont pas, et la population survivante penche
vers ceux qui ont bougé : à lire comme « à quelle vitesse la file sert ce qu'elle
contient », pas « combien de temps un candidat attend ».

**Réponse `200`** — `CandidateQueueLatencyReadModel`

```json
{
  "discoveredToScored": { "samples": 8421, "medianSeconds": 10800, "p90Seconds": 16560 },
  "scoredToValidated": { "samples": 512, "medianSeconds": 10800, "p90Seconds": 43200 },
  "retainedCandidates": 10400,
  "asOfUtc": "2026-08-05T12:00:00Z"
}
```

- Les percentiles sont `null` quand `samples` vaut 0 : aucune ligne ne portait les
  deux bornes du segment, ce qui n'est pas une latence nulle.
- `scoredToValidated` part vide au déploiement : `ValidatedAtUtc` n'était pas écrit
  avant #1024 (la promotion ne posait que le statut), donc le segment se remplit au
  fil des validations.

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
  "score": 92.4,
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
| Champions  | 16        | Public      |
| Truemains  | 11        | Public      |
| Ops        | 28        | `X-Ops-Key` |
| Infra      | 4         | —           |
| **Total**  | **59**    |             |

Décompte tenu à jour à la main contre les contrôleurs : 16 actions dans
`Controllers/Champions/ChampionsController.cs` (dont 2 en `POST`), 11 dans
`Controllers/Truemains/TruemainsController.cs`, 28 dans `Controllers/Ops/OpsController.cs`
(dont 2 en `POST`), plus les 4 routes d'infrastructure déclarées dans `Program.cs`.
