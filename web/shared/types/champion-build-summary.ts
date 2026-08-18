/**
 * The champion page's build, resolved to **names** server-side (#1123).
 *
 * Why a dedicated read model rather than reusing `ChampionResponse`: everything
 * the champion page shows is ids (item 3157, perk 8112), and turning those into
 * words needs `/api/static/items`, whose payload is ~373 KiB — deliberately
 * client-only (see `useStaticItems`). Server-rendering the page's build content
 * therefore cannot mean "SSR the existing fetches"; it means resolving the ids
 * away on the server and sending down only the handful of strings a crawler and
 * a first paint actually need. This model is that handful — ~3 KB (844 B
 * gzipped): names, one icon URL each, no descriptions, no ids the client would
 * have to look up again.
 *
 * Every field is nullable or empty-able on purpose. An id the static maps don't
 * know is **dropped**, never rendered as `Item 3157`: the whole point of the
 * block is to be the one part of the page a machine reads as prose, and a
 * placeholder that reads like a measurement is worse than a shorter sentence.
 */

/**
 * A game entity the summary prints by name. `id` is kept for `:key` only.
 *
 * `iconUrl` is the one thing this model carries beyond words (#1143): the block
 * renders each named entity with its own icon inline, so an item, a rune and a
 * summoner spell are told apart before the sentence is read. It is a URL per
 * named entity — twenty of them, ~2 KB raw and under 300 B of it after gzip,
 * since they share their CDN prefixes — not the static maps the ids came from, which is what keeps the payload the size the block was built for.
 * Null when the static payload had none: the name then stands alone rather than
 * reserving a gap for an icon that will never load.
 */
export interface SummaryEntity {
  id: number
  name: string
  iconUrl: string | null
}

export interface ChampionBuildSummary {
  championId: number
  /** Null when DDragon is unavailable — the block then renders nothing. */
  championName: string | null
  /** Resolved lane, echoed from the aggregate (not from the request). */
  position: string | null
  patch: string | null
  /** `ALL`, a bare tier, or a `<TIER>_PLUS` form — echoed from the aggregate. */
  eloBracket: string
  /**
   * Lane opponent the slice is scoped to (#923's `?vs=`), resolved to a name.
   * Null on the unfiltered page. Load-bearing: with an opponent pinned, the
   * panels above this block describe the *matchup's* build, so a paragraph that
   * didn't carry the opponent would confidently describe a different build than
   * the one on screen.
   */
  opponentName: string | null
  /**
   * Portrait for the pinned opponent. The only champion icon the block carries:
   * the champion the page is *about* is named by the heading right above the
   * paragraph, but a second champion appearing mid-sentence is the one place a
   * reader has to be told which one it is.
   */
  opponentIconUrl: string | null
  /**
   * False when the slice is below the trustworthy-build floor. The page already
   * flags it visually; the prose has to say it too, or the indexable version of
   * the page is the one that drops the caveat.
   */
  minSampleMet: boolean
  games: number
  wins: number
  winRate: number
  /** Null when the slice holds no build at all (a champion with no aggregate). */
  build: ChampionBuildSummaryBuild | null
  /**
   * Distinct builds the aggregate kept for this slice, `build` included. A
   * count, not a list: naming every build would restate the tabs above it, but
   * how *many* ways the champion is built is a real property of the lane and
   * varies between champions.
   */
  buildCount: number
}

export interface ChampionBuildSummaryBuild {
  /** Games behind *this build*, always ≤ the slice's `games`. */
  games: number
  winRate: number
  pickRate: number
  summonerSpells: SummaryEntity[]
  starterItems: SummaryEntity[]
  boots: SummaryEntity | null
  /** Completed core items, in build order. */
  coreItems: SummaryEntity[]
  keystone: SummaryEntity | null
  primaryStyle: SummaryEntity | null
  secondaryStyle: SummaryEntity | null
  /** The three minor perks of the primary tree, in slot order. */
  primaryRunes: SummaryEntity[]
  secondaryRunes: SummaryEntity[]
  /**
   * Max priority, not a 1-18 grid: the aggregate only carries which of Q/W/E is
   * levelled first, so this is at most three entries (see `Core/SkillOrder.vue`).
   * `name` is null when the champion's spell list didn't resolve — the key alone
   * is still meaningful.
   */
  skills: Array<{ key: string, name: string | null, iconUrl: string | null }>
}

/**
 * How a named entity is coloured in the paragraph (#1143).
 *
 * Not decoration: the five rune tones are the trees' own in-client colours, and
 * a League player reads "Domination" off the red before the word — the same
 * argument that keeps Riot's rank colours in `utils/tiers.ts`. Everything else
 * stays on the app's own vocabulary (see `main.css`), because an item and a
 * summoner spell are already told apart by the icon next to them.
 *
 * Carried as a semantic key rather than a class name so this stays a pure data
 * model: the mapping to utilities lives in the component that renders it, which
 * is also what keeps the class strings statically visible to Tailwind.
 */
export type BuildSummaryTone =
  | 'item'
  | 'spell'
  | 'ability'
  | 'champion'
  | 'precision'
  | 'domination'
  | 'sorcery'
  | 'inspiration'
  | 'resolve'
  /** A rune whose tree didn't resolve — named, but not attributed to a colour. */
  | 'rune'

/** Connective prose. Carries its own spacing, so tokens concatenate verbatim. */
export interface BuildSummaryTextToken {
  kind: 'text'
  text: string
}

/** A measurement: a count, a percentage, a patch, a rank scope. */
export interface BuildSummaryValueToken {
  kind: 'value'
  text: string
}

/** A named game entity, rendered with its icon and its tone. */
export interface BuildSummaryEntityToken {
  kind: 'entity'
  /**
   * What it reads as — not always the bare name: a collapsed run of the same
   * item is one token reading "two Health Potions", and an ability is
   * "Q (Five Point Strike)". The plain-text sentence is the concatenation of
   * these, so what a crawler reads and what a reader reads can never diverge.
   */
  text: string
  iconUrl: string | null
  tone: BuildSummaryTone
  /** `:key` for the rendered span. Not unique across a sentence on its own. */
  id: number | string
}

export type BuildSummaryToken =
  | BuildSummaryTextToken
  | BuildSummaryValueToken
  | BuildSummaryEntityToken

/** One sentence, in order. `championBuildSentences` is this, concatenated. */
export type BuildSummarySentence = BuildSummaryToken[]
