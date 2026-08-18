import type {
  BuildSummaryEntityToken,
  BuildSummarySentence,
  BuildSummaryToken,
  BuildSummaryTone,
  ChampionBuildSummary,
  ChampionBuildSummaryBuild,
  SummaryEntity,
} from '../types/champion-build-summary'
import type { ChampionResponse } from '../types/champions'
import type {
  ChampionStaticData,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '../types/static-data'
import { formatPercentage } from './ddragon'

/**
 * Resolution + prose for the server-rendered build summary (#1123). Pure and
 * dependency-free so it can be unit-tested away from Nitro and from the page:
 * the failure mode that matters here is not a crash but a *plausible wrong
 * sentence* — an item printed in the wrong order, a rune attributed to the wrong
 * tree, or a placeholder that reads like a measurement.
 */

/**
 * How each lane is said in a sentence. Deliberately not `POSITION_OPTIONS`'s
 * labels (`Middle`, `Support`): those are picker labels, and "wins 50.6% of
 * their games in the Support" is not English. Riot's `UTILITY` never surfaces.
 */
const LANE_PHRASE: Readonly<Record<string, string>> = {
  TOP: 'in the top lane',
  JUNGLE: 'in the jungle',
  MIDDLE: 'in the mid lane',
  BOTTOM: 'in the bot lane',
  UTILITY: 'as support',
}

/**
 * The lane as it reads mid-sentence, or `''` for an unknown/absent one so the
 * caller can drop the clause. Shared with the heading, which has to agree with
 * the paragraph under it.
 */
export function lanePhrase(position: string | null | undefined): string {
  return position ? LANE_PHRASE[position] ?? '' : ''
}

/**
 * How each elo filter is said. `ALL` adds nothing to a sentence — the absence of
 * a rank qualifier already means "every rank" — so it maps to the empty string
 * and the caller drops the clause entirely.
 */
function bracketPhrase(eloBracket: string): string {
  if (!eloBracket || eloBracket === 'ALL') return ''
  const [tier, plus] = eloBracket.split('_')
  if (!tier) return ''
  const named = tier.charAt(0) + tier.slice(1).toLowerCase()
  return plus === 'PLUS' ? `${named} and above` : named
}

/**
 * Small counts as words, so a sentence never opens with a numeral and a
 * repeated item reads as a quantity. Only ever called with 2 or more (one of
 * anything is worded without a count), hence the sparse start. Past single
 * digits it falls back to the figure rather than growing into a
 * number-to-words implementation.
 */
const NUMBER_WORDS: Readonly<Record<number, string>> = {
  2: 'two',
  3: 'three',
  4: 'four',
  5: 'five',
  6: 'six',
  7: 'seven',
  8: 'eight',
  9: 'nine',
}

function spellSmallNumber(value: number): string {
  return NUMBER_WORDS[value] ?? String(value)
}

function capitalise(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1)
}

/** Static entry shape the two resolvers below need — every static map fits it. */
interface NamedStatic {
  name: string
  iconUrl?: string | null
}

/**
 * Maps ids to `{ id, name, iconUrl }`, **dropping** every id the map doesn't
 * know rather than substituting a synthetic label. Order is the caller's order —
 * for the core path that is build order, which is load-bearing.
 *
 * The *name* is what makes an entry survive; the icon is optional and never
 * gates it. An entity DDragon named but shipped no artwork for is still a true
 * sentence, and dropping it would silently shorten a build path.
 */
function resolveMany<T extends NamedStatic>(
  ids: number[] | null | undefined,
  lookup: Record<number, T> | null | undefined,
): SummaryEntity[] {
  if (!ids?.length || !lookup) return []
  const resolved: SummaryEntity[] = []
  for (const id of ids) {
    const entry = lookup[id]
    if (entry?.name) resolved.push({ id, name: entry.name, iconUrl: entry.iconUrl || null })
  }
  return resolved
}

function resolveOne<T extends NamedStatic>(
  id: number | null | undefined,
  lookup: Record<number, T> | null | undefined,
): SummaryEntity | null {
  if (id == null || !lookup) return null
  const entry = lookup[id]
  return entry?.name ? { id, name: entry.name, iconUrl: entry.iconUrl || null } : null
}

export interface ResolveChampionBuildSummaryInput {
  championId: number
  champion: ChampionResponse | null
  championStatic: ChampionStaticData | null
  itemsMap: Record<number, StaticItemData> | null
  runeTree: RuneTreeResponse | null
  summonersMap: Record<number, StaticSummonerSpellData> | null
  /** Echoed when the aggregate is missing, so the model always states its scope. */
  requestedEloBracket: string
  /** Resolved name of the pinned lane opponent, or null when none is pinned. */
  opponentName?: string | null
  /** Portrait of that opponent, when the static payload carried one. */
  opponentIconUrl?: string | null
}

export function resolveChampionBuildSummary(
  input: ResolveChampionBuildSummaryInput,
): ChampionBuildSummary {
  const { champion, championStatic, itemsMap, runeTree, summonersMap } = input
  // `builds[0]` and not "the best build": `BuildTabs` opens on the first tab, so
  // any other choice would describe a build the reader isn't looking at.
  const build = champion?.builds?.[0] ?? null
  const core = build?.core ?? null
  const runePage = core?.runePage ?? null
  const perks = runeTree?.perks ?? null
  const perkStyles = runeTree?.perkStyles ?? null

  let summaryBuild: ChampionBuildSummaryBuild | null = null
  if (build && core) {
    const spells = core.summonerSpells
    summaryBuild = {
      games: build.games,
      winRate: build.winRate,
      pickRate: build.pickRate,
      summonerSpells: resolveMany(
        spells ? [spells.spell1Id, spells.spell2Id] : [],
        summonersMap,
      ),
      starterItems: resolveMany(core.starterItems?.itemIds, itemsMap),
      // The boots set carries a single pair in practice, but it is an item *set*
      // — take the first rather than assuming a scalar.
      boots: resolveOne(core.boots?.itemIds?.[0], itemsMap),
      coreItems: resolveMany(core.itemPath?.itemIds, itemsMap),
      keystone: resolveOne(runePage?.primaryKeystoneId, perks),
      primaryStyle: resolveOne(runePage?.primaryStyleId, perkStyles),
      secondaryStyle: resolveOne(runePage?.secondaryStyleId, perkStyles),
      primaryRunes: resolveMany(
        runePage ? [runePage.primaryPerk1Id, runePage.primaryPerk2Id, runePage.primaryPerk3Id] : [],
        perks,
      ),
      secondaryRunes: resolveMany(
        runePage ? [runePage.secondaryPerk1Id, runePage.secondaryPerk2Id] : [],
        perks,
      ),
      skills: (core.skillOrder?.sequence ?? []).map(key => ({
        key,
        name: championStatic?.championSpells?.[key]?.name ?? null,
        iconUrl: championStatic?.championSpells?.[key]?.iconUrl || null,
      })),
    }
  }

  return {
    championId: input.championId,
    championName: championStatic?.championName ?? null,
    position: champion?.position ?? null,
    patch: champion?.patch ?? null,
    eloBracket: champion?.eloBracket ?? input.requestedEloBracket,
    opponentName: input.opponentName ?? null,
    opponentIconUrl: input.opponentIconUrl ?? null,
    // Absent aggregate ⇒ no sample at all, which is not "sample met".
    minSampleMet: champion?.minSampleMet ?? false,
    games: champion?.totalGames ?? 0,
    wins: champion?.totalWins ?? 0,
    winRate: champion && champion.totalGames > 0
      ? champion.totalWins / champion.totalGames
      : 0,
    build: summaryBuild,
    buildCount: champion?.builds?.length ?? 0,
  }
}

/**
 * ─── The sentences, as tokens ───────────────────────────────────────────────
 *
 * The summary is the actual indexable text of the champion page, and since
 * #1143 it is also *typeset*: each named entity carries its icon and a tone, so
 * the paragraph is scannable the way the icon grid above it is.
 *
 * Tokens rather than a string the component parses back apart. A regex over
 * finished prose would have to find "Doran's Ring" inside a sentence that also
 * contains "Doran's Blade", and would break on the first item named after a
 * rune — the builder already knows exactly what each fragment *is*, so it says
 * so instead of hiding it in punctuation and making the view guess.
 *
 * `championBuildSentences` is the concatenation of these, which is what keeps
 * the sentence a crawler reads identical to the one a reader reads: there is
 * one builder, not a plain version and a decorated version that can drift.
 *
 * One rule survives from #1123 and still governs everything: a sentence is
 * emitted only when every figure in it is real. Nothing is padded to reach a
 * word count, because each sentence is a claim about a measurement, and a
 * generic filler sentence next to real numbers makes the real numbers read as
 * filler too.
 */

/**
 * Rune style id → tone. Riot's five trees, and the reason the tones exist at
 * all: a player reads "Domination" off the red before the word, exactly as they
 * read Gold off the rank colour (`utils/tiers.ts`). An id outside this map is
 * still named — it simply isn't attributed to a colour.
 */
const STYLE_TONES: Readonly<Record<number, BuildSummaryTone>> = {
  8000: 'precision',
  8100: 'domination',
  8200: 'sorcery',
  8300: 'inspiration',
  8400: 'resolve',
}

function styleTone(style: SummaryEntity | null | undefined): BuildSummaryTone {
  return (style ? STYLE_TONES[style.id] : undefined) ?? 'rune'
}

/** Connective prose. Every space in a sentence belongs to one of these. */
function plain(text: string): BuildSummaryToken {
  return { kind: 'text', text }
}

/** A measurement — a count, a percentage, a patch, a rank scope. */
function figure(text: string): BuildSummaryToken {
  return { kind: 'value', text }
}

function mark(
  entity: SummaryEntity,
  tone: BuildSummaryTone,
  text: string = entity.name,
): BuildSummaryEntityToken {
  return { kind: 'entity', text, iconUrl: entity.iconUrl, tone, id: entity.id }
}

/**
 * `a`, `a and b`, `a, b and c` — as tokens, separators included.
 *
 * `lastSeparator` is the whole difference between a list of *names* (` and `)
 * and a list of *clauses* (`, and `): the clauses are long enough that the
 * unpunctuated version misreads, which is why they keep the Oxford comma.
 */
function joinTokens(parts: BuildSummaryToken[][], lastSeparator: string): BuildSummaryToken[] {
  const tokens: BuildSummaryToken[] = []
  parts.forEach((part, index) => {
    if (index > 0) tokens.push(plain(index === parts.length - 1 ? lastSeparator : ', '))
    tokens.push(...part)
  })
  return tokens
}

/**
 * A list of entities, with runs of the same one collapsed into a count: a
 * starter set genuinely holds two potions as two entries, and "Health Potion and
 * Health Potion" is accurate but not English. The icon grid above is right to
 * repeat them; prose is not — and the collapsed token still carries the one
 * icon, so "two Health Potions" reads as a quantity rather than as two marks.
 *
 * Consecutive runs only, so the collapse can never reorder a list whose order is
 * the claim — the core path in particular, which never repeats an item anyway.
 */
function marks(entities: SummaryEntity[], tone: BuildSummaryTone): BuildSummaryToken[] {
  const parts: BuildSummaryToken[][] = []
  for (let i = 0; i < entities.length;) {
    const entity = entities[i]!
    let count = 1
    while (entities[i + count]?.id === entity.id) count++
    parts.push([mark(entity, tone, count > 1 ? `${spellSmallNumber(count)} ${entity.name}s` : entity.name)])
    i += count
  }
  return joinTokens(parts, ' and ')
}

export function championBuildSentenceTokens(summary: ChampionBuildSummary): BuildSummarySentence[] {
  const name = summary.championName
  if (!name || summary.games === 0) return []

  const sentences: BuildSummarySentence[] = []
  const lane = lanePhrase(summary.position)
  const bracket = bracketPhrase(summary.eloBracket)

  const scope: BuildSummaryToken[] = [
    plain('Across '),
    figure(summary.games.toLocaleString('en-US')),
    plain(' ranked games'),
  ]
  if (summary.patch) scope.push(plain(' on patch '), figure(summary.patch))
  if (bracket) scope.push(plain(' in '), figure(bracket))
  scope.push(
    plain(`, ${name} mains win `),
    figure(formatPercentage(summary.winRate)),
    plain(` of their games${lane ? ` ${lane}` : ''}`),
  )
  if (summary.opponentName) {
    scope.push(plain(' against '), {
      kind: 'entity',
      text: summary.opponentName,
      iconUrl: summary.opponentIconUrl,
      tone: 'champion',
      id: 'opponent',
    })
  }
  scope.push(plain('.'))
  sentences.push(scope)

  // Second, so it qualifies everything after it. The page flags a thin slice
  // with a warning icon; without this the *indexable* version of the page would
  // be the one that states the figures unqualified.
  if (!summary.minSampleMet) {
    sentences.push([plain(
      'That is below the sample TrueMain requires before it treats a build as settled, so read the figures here as indicative.',
    )])
  }

  const build = summary.build
  if (!build) return sentences

  // The build's own share and record, stated before its contents: the contents
  // are only worth reading once you know how much of the sample chose them.
  // Its own sentence rather than an aside inside the next one, which is what
  // the em dashes used to buy — they read as a stumble in a narrow column, and
  // splitting also means the share survives a build with no item data at all.
  sentences.push([
    plain('Their most common build appears in '),
    figure(formatPercentage(build.pickRate)),
    plain(' of those games and wins '),
    figure(formatPercentage(build.winRate)),
    plain(' of its '),
    figure(build.games.toLocaleString('en-US')),
    plain(build.games === 1 ? ' game.' : ' games.'),
  ])

  const itemClauses: BuildSummaryToken[][] = []
  if (build.starterItems.length) {
    itemClauses.push([plain('starts '), ...marks(build.starterItems, 'item')])
  }
  if (build.coreItems.length) {
    itemClauses.push([plain('completes '), ...marks(build.coreItems, 'item'), plain(' in that order')])
  }
  if (build.boots) itemClauses.push([plain('takes '), mark(build.boots, 'item')])
  if (itemClauses.length) {
    sentences.push([plain('It '), ...joinTokens(itemClauses, ', and '), plain('.')])
  }

  if (build.keystone) {
    // Built as two clauses joined by a comma rather than one chain of `and`s:
    // the primary tree already needs an `and` inside its own rune list, so
    // appending the secondary with another one produced "… and Treasure Hunter
    // and Sorcery secondary …", which reads as a single four-item list.
    const primaryTone = styleTone(build.primaryStyle)
    const tokens: BuildSummaryToken[] = [plain('It runs '), mark(build.keystone, primaryTone)]
    if (build.primaryStyle) tokens.push(plain(' out of '), mark(build.primaryStyle, primaryTone))
    if (build.primaryRunes.length) tokens.push(plain(' with '), ...marks(build.primaryRunes, primaryTone))
    if (build.secondaryStyle) {
      const secondaryTone = styleTone(build.secondaryStyle)
      tokens.push(plain(', and '), mark(build.secondaryStyle, secondaryTone), plain(' secondary'))
      if (build.secondaryRunes.length) {
        tokens.push(plain(' for '), ...marks(build.secondaryRunes, secondaryTone))
      }
    }
    tokens.push(plain('.'))
    sentences.push(tokens)
  }

  if (build.skills.length) {
    const chain = build.skills.map((skill): BuildSummaryEntityToken => ({
      kind: 'entity',
      text: skill.name ? `${skill.key} (${skill.name})` : skill.key,
      iconUrl: skill.iconUrl,
      tone: 'ability',
      id: skill.key,
    }))
    const [first, ...rest] = chain
    if (rest.length) {
      const tokens: BuildSummaryToken[] = [plain('Abilities are levelled '), first!, plain(' first')]
      for (const token of rest) tokens.push(plain(', then '), token)
      tokens.push(plain('.'))
      sentences.push(tokens)
    }
    else {
      sentences.push([first!, plain(' is levelled first.')])
    }
  }

  if (build.summonerSpells.length) {
    sentences.push([
      plain('Summoner spells are '),
      ...marks(build.summonerSpells, 'spell'),
      plain('.'),
    ])
  }

  const others = summary.buildCount - 1
  if (others > 0) {
    sentences.push([plain(
      others === 1
        ? `One other build is played often enough ${lane || 'on this lane'} to be measured on its own.`
        : `${capitalise(spellSmallNumber(others))} other builds are played often enough ${lane || 'on this lane'} to be measured on their own.`,
    )])
  }

  return sentences
}

/** One sentence's tokens, concatenated — what a crawler and a screen reader get. */
export function buildSummarySentenceText(sentence: BuildSummarySentence): string {
  return sentence.map(token => token.text).join('')
}

/**
 * The summary as plain sentences. Derived from the tokens rather than built
 * alongside them, so the decorated paragraph and its text can never disagree.
 */
export function championBuildSentences(summary: ChampionBuildSummary): string[] {
  return championBuildSentenceTokens(summary).map(buildSummarySentenceText)
}
