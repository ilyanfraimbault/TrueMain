# TrueMain glossary

The project's language: what each League of Legends and TrueMain term means, and which word to use when several
compete. Domain only — no class, table or endpoint names; thresholds live in the decision log and the code, not
here. Maintained with the `domain-modeling` skill.

## Words that compete

- **Position** is the canonical word for where a player plays (top, jungle, middle, bottom, utility). "Role" is
  fine in UI copy; "lane" means the laning phase (lane win rate, lane leads), never the position of a jungler.
- **Elo bracket** is the bucket a game is counted in; **rank** is a player's standing; **ranked tier** is Riot's
  ladder step (Iron…Challenger); **champion tier** is the S–D grade. "Tier" alone is ambiguous: qualify it.
- **Match** is the record fetched from Riot and stored; **game** is the unit counted and shown ("120 games").
- **Platform** (euw1, kr, na1) is what the UI calls "region"; a **regional route** (europe, americas, asia) is a
  different thing, used only for Riot calls.
- **Lane** also names the two halves of the ingestion pipeline: always say **fetch lane** / **aggregate lane**.

## Players and mains

**True main**:
A player with sustained and current investment in one champion, measured by their play rate on it over recent
ranked solo/duo games. Champion statistics are built from true mains by default. "Truemains" (one word) in UI copy.
_Avoid_: OTP (a narrower verdict), "main" unqualified in UI copy

**OTP**:
A true main whose play rate on the champion is so high they almost only play it. A verdict and a badge, a subset
of true mains.
_Avoid_: one-trick as a synonym for true main

**Extended sample**:
True mains admitted under a lowered play-rate bar because their champion has too few mains; labelled as such.

**Active / inactive main**:
Whether the player still plays the champion. An inactive main is retired by a flag, never deleted.

**Retired sample**:
A main whose games have aged out of retention: their figures stay, dated as a past measurement. Distinct from
inactive.

**Signature champion**:
A player's most-played main; the champion their Truemain score, profile and leaderboard row talk about.

**Tracked account**:
A Riot account the pipeline knows and ingests, identified by its PUUID (name#tag is not unique).

**Candidate**:
A (player, champion) pair proposed as a possible true main, found on the apex ladder, added by an operator, or
harvested from games already stored. It moves New → Scored → Queued → Processing → Validated.

**Main analysis**:
The decision, per account, champion and platform, of whether the player is a true main or an OTP.

**Cohort**:
Whose games count in a champion page's figures: a tracked true main of the champion played, in a canonical
position, in a game that is not a remake.

**Remake**:
A game ended in its first minutes; never counted as a game.

## Ranks and populations

**Elo bracket**:
The ranked-tier bucket a game is counted in: the player's tier when the game was played. "All" is a union read
at query time; "Tier+" means this tier and above. Master+ is the champion pages' default.
_Avoid_: elo, rank bracket, rank scope

**Rank**:
A player's ranked standing: ranked tier, division and LP, captured at most once a day.

**Solo/Duo tier**:
The player's ranked tier in ranked solo/duo, the only queue TrueMain stores.

**Population**:
Whose games a champion figure is built from: true mains (the default) or everyone tracked (a strict superset).
The page header always names it.

## Champion statistics

**Aggregate**:
A stored, pre-computed slice of champion statistics per player, champion, patch, platform, queue, position and
elo bracket. Aggregates are the site's history once raw matches are deleted.

**Pattern**:
One observed combination of build, rune page, skill order, summoner spells and starter items in an aggregate;
each match counts in exactly one.

**Dimension**:
A deduplicated build component (build, rune page, skill order, spell pair, starter basket), identified by a
canonical key.

**Fold**:
Counting each match once, incrementally, into an aggregate. A **live fold** counts raw games at request time
instead.
_Avoid_: recompute

**Re-fold**:
Wiping and re-folding an aggregate; it can only recover live patches.

**Replace-by-scope**:
Refreshing champion aggregates by rebuilding the scopes of live patches only; frozen patches are never touched.

**Live patch / frozen patch**:
A live patch still has its raw matches retained and can be re-folded; a frozen patch has lost them, and its
aggregates are permanent history.

**Served patch**:
The patch the site shows by default: the newest with enough data to fill the champion directory.
_Avoid_: current patch, latest patch (when this is meant)

**Sample**:
The number of games a figure rests on. A **thin sample** below its floor shows with a caveat, never a 404.

**Build**:
A distinct item path played on a champion; **core** is the most-played option of each dimension inside a build.

**Build archetype**:
The class of completed items a build leans on: crit, armour penetration, on-hit, AP, tank, or none.
_Avoid_: item archetype

**Champion tier**:
A champion's S–D grade within one of its dominant lanes on a patch, presence weighted before win rate.

**Dominant lane**:
One of a champion's (at most two) most-played positions.

**Matchup**:
A champion against the **role opponent**, the enemy who held the same position.
_Avoid_: lane opponent (a jungler has no lane)

**Lane outcome**:
How a lane stood at 15 minutes: won, lost or undecided. **Lane win rate** counts decided lanes only.

**Synergy**:
How much better a champion does with a same-team partner than expected — observed minus expected win rate,
never the raw pair win rate.

**Champion profile**:
What a champion measurably does on a lane and patch (damage split, healing, crowd control, tankiness, lane
leads), measured from games, never hand-labelled. Its **damage profile** is the physical / magic / true split.

**Item context**:
Why an item is built: each build step is **Core** (nearly always), **Situational** (a measurable situation — a
**draft axis** such as enemy magic damage — moves its pick rate) or **Preference** (taste, shown as nothing).

**Next item**:
The completed item true mains build next from where the player stands — what mains choose, not the best win rate.

**Pace benchmark**:
The distribution of a laner's CS and gold minute by minute, per patch, ranked tier and position, against which
the overlay compares the player.

## Scores

**Truemain score**:
How much a champion is "yours", from 0 to 100, from play-rate commitment and mastery. Called dedication score in
the code and the API.
_Avoid_: dedication score in UI copy

**Performance score**:
How well one player played one game, from 0 to 100, from components weighted by position; decides the MVP/ACE
accolade and the placement.

**Candidate score**:
The blend that ranks candidates for promotion into the ingestion queue.

## Ingestion pipeline

**Process**:
One named step of the ingestion pipeline; each execution is a **process run**.

**Fetch lane / aggregate lane**:
The two halves of the pipeline: the fetch lane is bound by Riot calls, the aggregate lane by the database.

**Claim**:
Match ingestion taking a batch of accounts to fetch; it sizes every upstream budget (**intake capacity**).

**Lease**:
The time-bounded hold a claim places on an account's candidates while they are fetched.

**Established main**:
An already-validated true main, as opposed to a new candidate, in a claim.

**New candidate**:
In a claim, an account never ingested that holds a `Queued` candidate — the breadth class; an already-ingested
account is never one (#1535).

**Coverage**:
Active true mains per platform and champion; the **coverage deficit** of a platform allocates every budget
between platforms.

**Data quality**:
The admin's health checks and detectors, which should read zero on healthy data.

## Draft and desktop

**Draft tool**:
The web matchup page: your champion, position and role opponent, plus optional draft slots, give a build from
similar drafts.
_Avoid_: builder

**Champion select**:
The desktop app's live view of the League client's draft, with pick and ban suggestions each ranked on a
measured reason. Never a win probability.

**Blind safety**:
How safe a pick is while the role opponent is still unknown.

**Game page**:
The desktop view during a live game: each player's standing, role fit, record on their champion and the
**TrueMain mark** (the badge on a player who is a true main of their champion).

**Role fit**:
A player's relation to the position they are playing: main role, secondary role, or **autofill**.

**Lane edge**:
The game page's per-lane arrow and chance towards the favoured side.

**Overlay**:
The in-game panels drawn over League (next item, win probability, pace, item value); click-through.

**Win probability**:
An in-game estimate from lane leads and map objectives; overlay and post-game only.

**Dashboard**:
The desktop home page: the player's own record, read from their League client.
