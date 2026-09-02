# Riot production API key — application checklist

What has to be true before the production key application is submitted on the
Riot developer portal, and what the application itself must say. The switch that
follows an approval is a separate, destructive operation with its own runbook:
[`docs/riot-key-switch.md`](riot-key-switch.md).

## Why apply

TrueMain runs on a **permanent personal key** — not a development key: it does
not expire every 24 hours, and it is the key both prod and preprod (its own)
have always used. It has never been approved for production, which is what caps
the pipeline:

| | Personal key (today) | Production key (documented starting allocation) |
| --- | --- | --- |
| Burst | 20 requests / 1 s | 500 requests / 10 s |
| Sustained | 100 requests / 2 min | 30 000 requests / 10 min |

Both are **per routing value**, so the six routing values we touch each get
their own budget. The sustained figure is the one that matters: 100 per 2
minutes is 50 per minute, against 3 000 per minute on an approved key — a
factor of 60 on the number that actually gates ingestion. At roughly 2–2.5 Riot
calls per stored match, that is the difference between the ~15 k matches a day
we ingest now and the volume the established analytics sites process on a patch.
No engineering on our side moves that ceiling; Riot raises it further for
products that demonstrate community benefit.

The production key is also the precondition for two features that are otherwise
permanently blocked: RSO sign-in (#780) and anything requiring endpoints
restricted to approved products.

## Checklist before submitting

Riot reviews the *product*, not the code. Each item below is a thing a reviewer
can open in a browser or observe on our traffic.

### The site

- [ ] **A working site on a verified domain.** `truemain.lol`, served over
      HTTPS, with the domain verified on the developer portal from the account
      that owns the current key. A reviewer landing on an error page or a
      half-built section is the most common rejection.
- [ ] **Terms of Service page**, publicly reachable and linked from the site
      footer — `web/app/pages/terms.vue`, `/terms`.
- [ ] **Privacy policy page**, same treatment — `web/app/pages/privacy.vue`,
      `/privacy`. It must state what player data is stored, why, how long, and
      how a player asks for its deletion, with a contact route that works.
- [ ] **Riot legal boilerplate** — the "not endorsed by Riot Games" disclaimer,
      present and legible (`/about`, `web/app/pages/about.vue`).
- [ ] **A screenshot set**: the champion page (builds, runes, matchups), a
      player page, and the search. The application asks for evidence that the
      product exists and is used, not mockups.

### The behaviour

- [ ] **A proactive rate limiter that reads Riot's own headers**
      (`X-App-Rate-Limit`, `X-App-Rate-Limit-Count`, `X-Method-Rate-Limit`,
      `X-Method-Rate-Limit-Count`) and paces requests *before* being refused —
      issue #1359. This is a prerequisite, not a nicety: "respects the rate
      limits" is an explicit condition of the production key, and the same
      limiter is what will pick up the new, larger limits automatically after
      the switch, since it reads them from the headers rather than from
      configuration.
- [ ] **429 rate below 0.1 % of calls for a full week** before submitting,
      measured on the admin portal's Riot API usage panel. A history of retrying
      into 429s is the behaviour the requirement exists to exclude.
- [ ] **No dependency on spectator-v5.** Riot announced its retirement
      (DevRel, October 2025), so an application built around live-game features
      argues for a product that will not work. Issue #532 (live games for
      tracked mains) is **parked** for this reason and must not appear in the
      product description or the screenshots.

### The data obligations

- [ ] **A purge-by-PUUID path.** Riot relays GDPR deletion requests to
      production key holders as lists of identifiers, and the holder is expected
      to act on them. Today the ops API (`/ops`, `Api/Controllers/Ops`) has no
      such endpoint — deletion would be a manual SQL sweep across
      `riot_accounts`, `match_participants` (whose `"RiotAccountId"` foreign key
      does *not* cascade, unlike `rank_snapshots`), `main_champion_stats`,
      `main_candidates` and `seed_requests`. A small
      authenticated ops endpoint that takes a batch of PUUIDs and deletes every
      trace of them, transactionally and idempotently, is the piece to build
      before the key is granted. It is deliberately **not** part of this
      documentation PR: it deletes production player data and deserves its own
      issue, tests and review.
- [ ] **Retention that we can describe truthfully.** The privacy policy and the
      application must match what `MatchDataRetention` actually does (raw match
      data kept for the live patches; accounts and mains kept).

## The product description

Riot asks for a short description of what the product does and who it helps. The
framing they respond to is **helping players improve** — not "an analytics
platform", not "a data aggregator". One paragraph, roughly:

> TrueMain helps League of Legends players get better on the champions they
> actually play. For every champion it shows what winning players build, rune
> and skill through, how the matchup and lane phase typically go, and where the
> power spikes fall; for a player it shows how their own games on that champion
> diverge from what works, so they know what to change. The data comes from
> ranked solo/duo matches ingested through the Riot API and is presented free,
> without advertising interstitials or paywalled statistics.

Adjust the wording, keep the two properties: it is addressed to a player trying
to improve, and it is honest about the scope (ranked solo/duo only — see the
ranked-only storage decision).

## Submitting

The application is filed on the Riot developer portal, **from the account that
owns the current key**, and it is the maintainer's to submit — it involves
signing in to that account and accepting Riot's terms, neither of which can be
delegated.

Riot states a 1–3 week review; cases regularly run six weeks or more, and a
request for clarification restarts the clock. So the application goes out as
soon as the checklist above is green rather than after everything else in the
epic ships. Track the submission date and each status change on #1363.

## After approval

1. Run [`docs/riot-key-switch.md`](riot-key-switch.md) — rehearsal on preprod
   first. Every stored PUUID is invalidated by the new key; this is not
   optional reading.
2. Let the limiter pick up the new limits from the response headers; nothing
   should need to be configured by hand.
3. Raise the throughput knobs **in steps**, watching Postgres between each:
   `MatchIngestion:BatchSize`, the intake cadence, and the aggregate lane. The
   Riot ceiling stops being the bottleneck at that point and the database
   becomes the next wall — index size, cycle times and cold latency are the
   gauges to watch.
4. Reconsider the features the personal key blocked: RSO (#780). Not
   spectator-v5 (#532), which is being retired regardless.

## Related

- #1363 — this application; #1357 — the data-pipeline audit epic it belongs to.
- #1359 — the proactive rate limiter (prerequisite).
- #788 / #789 — PUUIDs are key-scoped, and the Riot ID backfill that makes the
  switch survivable.
- #780 — RSO, which also requires a production key.
- #532 — live games, parked: spectator-v5 is being retired.
