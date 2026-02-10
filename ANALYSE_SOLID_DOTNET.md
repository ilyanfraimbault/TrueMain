# Analyse globale du code - SOLID et conventions .NET

Date: 2026-02-09
Projet: TrueMain

## 1) Perimetre et methode

Cette analyse couvre l'ensemble du code applicatif de la solution:
- `Api`
- `Ingestor`
- `Data`
- `Core`

Elements verifies:
- Lecture complete des fichiers C# applicatifs (hors code genere EF dans `Data/Migrations`)
- Lecture des fichiers de configuration principaux (`appsettings`, `Dockerfile`, `compose.yaml`, `csproj`)
- Build de la solution: `dotnet build TrueMain.sln` (OK, 0 warning, 0 error)
- Test de la solution: `dotnet test TrueMain.sln` (aucun projet de test detecte)

## 2) Resume executif

Niveau global (estimation): **6.5 / 10**

Le projet est bien segmente en couches/projets et compile proprement. Les bases sont bonnes (DI, interfaces de repositories, workflow clair des processus), mais plusieurs points freinent l'alignement "maximum SOLID" et conventions .NET de production:
- classes de processus tres volumineuses avec responsabilites multiples,
- logique dupliquee (clients Riot HTTP),
- risques de concurrence dans le claim des comptes,
- validations d'options insuffisantes,
- observabilite inegale (retours anticipes sans enregistrement systematique des runs),
- absence de tests automatiques.

## 3) Points forts

- Separation de solution claire par responsabilite (`Api`, `Ingestor`, `Data`, `Core`).
- Bonne utilisation de l'injection de dependances dans les processus (`IRiot*Client`, `IDataSessionFactory`).
- Utilisation coherente de `CancellationToken` dans la majorite des chemins async.
- Logging structure (templates et proprietes) deja en place.
- EF Core configure avec indexes utiles et modelisation explicite.
- Flux metier global comprehensible: discovery -> scoring -> ingestion -> validation -> main analysis.

## 4) Constats prioritaires (risques et ecarts)

## [Critique] Claim concurrent potentiellement non atomique pour l'ingestion

References:
- `Data/Repositories/RiotAccountRepository.cs:53`
- `Data/Repositories/RiotAccountRepository.cs:79`
- `Ingestor/Processes/MatchIngestionProcess.cs:215`

Observation:
- Le claim des comptes repose sur une selection d'entites `Idle`, puis mutation en memoire (`entry.ra.MatchIngestStatus = Processing`) avant `SaveChanges`.
- En execution multi-instance, deux workers peuvent lire le meme jeu "Idle" avant commit, donc reclamer les memes comptes.

Impact:
- Double traitement possible, contention DB, etats incoherents, charge API inutile.

Recommendation:
- Passer a un claim atomique SQL (update conditionnel + retour des lignes affectees) avec verrouillage explicite ou strategie optimistic robuste.
- Exemple de strategie: `UPDATE ... SET status='Processing' WHERE status='Idle' ... RETURNING ...` dans une transaction.

## [Important] Classes "god process" (SRP fragilise)

References:
- `Ingestor/Processes/MatchIngestionProcess.cs:12` (484 lignes)
- `Ingestor/Processes/DiscoveryProcess.cs:12` (315 lignes)
- `Ingestor/Processes/MainAnalysisProcess.cs:9` (223 lignes)

Observation:
- Les processus cumulent orchestration, regles metier, mapping DTO -> entites, persistence, transitions d'etat, gestion d'erreurs et reporting.

Impact:
- Testabilite faible, complexite cyclomatique elevee, risque de regressions lors des evolutions.

Recommendation:
- Extraire des services specialises par responsabilite.
- Exemples: `IMatchClaimService`, `IMatchSnapshotMapper`, `ITimelineApplier`, `IMainStatsCalculator`, `ICandidateStatusService`.

## [Important] Duplication de logique HTTP/Retry dans les clients Riot

References:
- `Ingestor/Riot/RiotPlatformClient.cs:75`
- `Ingestor/Riot/RiotMatchClient.cs:59`
- `Ingestor/Riot/RiotAccountClient.cs:45`

Observation:
- Meme algorithme de retry 429 + lecture JSON + parsing `Retry-After` recopie dans 3 classes.

Impact:
- Cout de maintenance eleve, derive de comportement probable a moyen terme.

Recommendation:
- Centraliser dans un composant commun (ex: `RiotHttpExecutor`) ou policy handler `HttpClientFactory`.
- Ajouter timeout/policies explicites (retry exponentiel + jitter, circuit breaker selon besoins).

## [Important] Observabilite partielle des runs

References:
- `Ingestor/Processes/ScoringProcess.cs:60`
- `Ingestor/Processes/MainAnalysisProcess.cs:37`
- `Ingestor/Processes/AccountRefreshProcess.cs:30`
- `Ingestor/Processes/DiscoveryProcess.cs:27`

Observation:
- Plusieurs retours anticipes sortent sans `ProcessRun` en base (ou pas de maniere uniforme).

Impact:
- Historique incomplet des executions, suivi ops et debugging moins fiables.

Recommendation:
- Garantir un enregistrement systematique (success/no-op/failure) pour chaque run de processus.

## [Important] Validation de configuration insuffisante (Options)

References:
- `Ingestor/Program.cs:13`
- `Ingestor/Options/*.cs`

Observation:
- Les options sont bindees mais non validees au demarrage (`ValidateOnStart` absent, pas de DataAnnotations/validators).
- Le code rattrape ensuite avec des `Math.Max` et comportements par defaut.

Impact:
- Erreurs de config detectees tardivement, comportements silencieux difficiles a diagnostiquer.

Recommendation:
- Ajouter validation stricte des options:
  - bornes (`BatchSize > 0`, poids de scoring >= 0, etc.)
  - champs obligatoires (`Riot:ApiKey`)
  - `ValidateOnStart()`.

## [Amelioration] `TrueMainDbContext` monolithique (maintenabilite)

References:
- `Data/TrueMainDbContext.cs:21`

Observation:
- Toute la configuration EF est dans un seul fichier de 446 lignes.

Impact:
- Diffs lourds, relecture difficile, couplage fort entre entites.

Recommendation:
- Decouper par `IEntityTypeConfiguration<T>` (un fichier par entite), puis `ApplyConfigurationsFromAssembly`.

## [Amelioration] `IDataSession` trop large pour l'ISP

References:
- `Data/Repositories/IDataSession.cs:5`

Observation:
- Interface agregee expose tous les repositories, meme quand un processus n'en utilise qu'une partie.

Impact:
- Dependance plus large que necessaire, mock plus verbeux en tests.

Recommendation:
- Soit conserver (si choix volontaire "Unit of Work")
- Soit introduire des interfaces focalisees par use-case quand la base de code grandit.

## [Amelioration] `DataSession` instancie directement les repositories (DIP partiel)

References:
- `Data/Repositories/DataSession.cs:12`

Observation:
- `new MainCandidateRepository(_db)` etc. dans `DataSession`.

Impact:
- Fermeture a l'extension, substitution plus couteuse.

Recommendation:
- Injecter les repositories via DI, ou encapsuler leur creation dans une factory dediee.

## [Amelioration] Worker pilote les modes par switch string

References:
- `Ingestor/Worker.cs:27`
- `Ingestor/Worker.cs:66`

Observation:
- Le mode est une string normalisee puis `switch` sur enum interne, avec fallback silencieux sur `Full`.

Impact:
- Typos de config non visibles, extension necessitant modification centrale.

Recommendation:
- Utiliser enum d'options validee + warning explicite sur valeur inconnue.
- Evoluer vers un pipeline de "job steps" extensible (`IJobStep`).

## [Amelioration] Conventions .NET / hygiene projet

References:
- `Api/Controllers/WeatherForecastController.cs:1`
- `Api/WeatherForecast.cs:1`
- absence de `.editorconfig`, `Directory.Build.props`, regles analyzers

Observation:
- Code template ASP.NET encore present dans l'API.
- Pas de configuration d'analyse statique/qualite unifiee a l'echelle solution.

Impact:
- Bruit fonctionnel et standards de style/qualite non forces.

Recommendation:
- Supprimer le code template non utilise.
- Ajouter un baseline de qualite: analyzers .NET, regles style, severite warning.

## 5) Evaluation SOLID detaillee

### S - Single Responsibility Principle

Etat: **partiellement respecte**

- Positif: separation macro par projets et processus metiers distincts.
- Ecart principal: processus trop gros et multi-responsabilites.

Cible:
- Garder les processus comme orchestrateurs uniquement.
- Extraire calcul metier, mapping et persistence en services testables.

### O - Open/Closed Principle

Etat: **partiellement respecte**

- Positif: contrats d'acces aux donnees et clients externes via interfaces.
- Ecart: ajout d'un nouveau mode/process exige modification du `Worker`; logique retry dupliquee.

Cible:
- Pipeline extensible de steps + composant retry mutualise.

### L - Liskov Substitution Principle

Etat: **globalement respecte (peu d'heritage)**

- Peu de hierarchies de classes polymorphes sensibles.
- Vigilance sur la clarte semantique des contrats repository (claim/statut) pour eviter effets de bord implicites.

### I - Interface Segregation Principle

Etat: **moyen**

- Positif: interfaces repository plutot petites.
- Ecart: `IDataSession` expose un aggregate large.

Cible:
- Introduire des interfaces plus fines quand le besoin test/perimetre augmente.

### D - Dependency Inversion Principle

Etat: **plutot bon, avec points a corriger**

- Positif: processus dependants d'abstractions (`IRiot*Client`, `IDataSessionFactory`).
- Ecart: instanciation concrete des repositories dans `DataSession`; orchestration worker couplage fort aux classes concretes.

Cible:
- Uniformiser la dependance sur abstractions jusque dans la composition interne.

## 6) Evaluation conventions .NET

### Points conformes

- Async/await correctement utilise dans la majorite des appels I/O.
- `CancellationToken` present de bout en bout dans la plupart des APIs.
- Logging structure avec placeholders.
- Utilisation de `IOptions<T>` et `HttpClientFactory`.

### Ecarts principaux

- Validation d'options insuffisante au startup.
- Pas de socle analyzers/style centralise.
- Absence de tests unitaires/integration.
- DbContext tres volumineux.
- Incoherence d'observabilite sur les runs "no-op".

## 7) Plan d'amelioration recommande (ordre conseille)

## Phase 1 - Quick wins (faible risque, fort ROI)

- Ajouter validation forte des options + `ValidateOnStart`.
- Enregistrer tous les runs (success/no-op/failure) de maniere uniforme.
- Supprimer le template API non utilise (`WeatherForecast*`).
- Introduire analyzers + `.editorconfig` + severite warnings.

## Phase 2 - Fiabilite runtime

- Refactor du claim de comptes en operation atomique SQL.
- Renforcer resilience HTTP (timeouts, retry policy centralisee, jitter).
- Standardiser les transitions d'etat (service unique de state transitions).

## Phase 3 - Refactor SOLID structurel

- Decouper `MatchIngestionProcess`, `DiscoveryProcess`, `MainAnalysisProcess` en composants SRP.
- Factoriser les clients Riot sur un noyau commun.
- Migrer `DbContext` vers configurations par entite.

## Phase 4 - Qualite continue

- Ajouter tests unitaires ciblant d'abord:
  - calcul de score,
  - calcul des stats "main",
  - transitions de statut (Queued/Processing/Validated/Scored),
  - mapping timeline/item/skill events.
- Ajouter tests d'integration DB pour le claim concurrent.

## 8) Conclusion

Le projet est deja exploitable et lisible, avec une bonne base d'architecture. Pour atteindre un niveau "maximum SOLID + conventions .NET", la priorite est de:
1. securiser la concurrence et l'observabilite,
2. reduire la taille/responsabilites des processus,
3. imposer des garde-fous de qualite (validation config, analyzers, tests).

Ces actions peuvent etre menees de facon incrementale sans rearchitecture totale, en preservant le workflow metier actuel.
