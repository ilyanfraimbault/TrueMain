import type {
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

/** Sentence-cased join: `a`, `a and b`, `a, b and c`. */
function listPhrase(parts: string[]): string {
  if (parts.length <= 1) return parts[0] ?? ''
  return `${parts.slice(0, -1).join(', ')} and ${parts[parts.length - 1]}`
}

/**
 * Entity names for a sentence, with runs of the same item collapsed into a
 * count: a starter set genuinely holds two potions as two entries, and
 * "Health Potion and Health Potion" is accurate but not English. The icon grid
 * is right to repeat them; prose is not.
 *
 * Consecutive runs only, so the collapse can never reorder a list whose order
 * is the claim — the core path in particular, which never repeats an item
 * anyway.
 */
function names(entities: SummaryEntity[]): string {
  const parts: string[] = []
  for (let i = 0; i < entities.length;) {
    const entity = entities[i]!
    let count = 1
    while (entities[i + count]?.id === entity.id) count++
    parts.push(count > 1 ? `${spellSmallNumber(count)} ${entity.name}s` : entity.name)
    i += count
  }
  return listPhrase(parts)
}

/**
 * Maps ids to `{ id, name }`, **dropping** every id the map doesn't know rather
 * than substituting a synthetic label. Order is the caller's order — for the
 * core path that is build order, which is load-bearing.
 */
function resolveMany<T extends { name: string }>(
  ids: number[] | null | undefined,
  lookup: Record<number, T> | null | undefined,
): SummaryEntity[] {
  if (!ids?.length || !lookup) return []
  const resolved: SummaryEntity[] = []
  for (const id of ids) {
    const name = lookup[id]?.name
    if (name) resolved.push({ id, name })
  }
  return resolved
}

function resolveOne<T extends { name: string }>(
  id: number | null | undefined,
  lookup: Record<number, T> | null | undefined,
): SummaryEntity | null {
  if (id == null || !lookup) return null
  const name = lookup[id]?.name
  return name ? { id, name } : null
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
 * The summary as sentences — the actual indexable text of the champion page.
 *
 * One rule throughout: a sentence is emitted only when every figure in it is
 * real. Nothing is padded to reach a word count, because each sentence is a
 * claim about a measurement, and a generic filler sentence next to real numbers
 * makes the real numbers read as filler too.
 */
export function championBuildSentences(summary: ChampionBuildSummary): string[] {
  const name = summary.championName
  if (!name || summary.games === 0) return []

  const sentences: string[] = []
  const lane = lanePhrase(summary.position)
  const bracket = bracketPhrase(summary.eloBracket)

  const scope = [
    `Across ${summary.games.toLocaleString('en-US')} ranked games`,
    summary.patch ? `on patch ${summary.patch}` : '',
    bracket ? `in ${bracket}` : '',
  ].filter(Boolean).join(' ')
  const versus = summary.opponentName ? ` against ${summary.opponentName}` : ''
  sentences.push(
    `${scope}, ${name} mains win ${formatPercentage(summary.winRate)} of their games${lane ? ` ${lane}` : ''}${versus}.`,
  )

  // Second, so it qualifies everything after it. The page flags a thin slice
  // with a warning icon; without this the *indexable* version of the page would
  // be the one that states the figures unqualified.
  if (!summary.minSampleMet) {
    sentences.push(
      'That is below the sample TrueMain requires before it treats a build as settled, so read the figures here as indicative.',
    )
  }

  const build = summary.build
  if (!build) return sentences

  // The build's own share and record, stated before its contents: the contents
  // are only worth reading once you know how much of the sample chose them.
  const opener = `Their most common build — ${formatPercentage(build.pickRate)} of those games, ${formatPercentage(build.winRate)} win rate over ${build.games.toLocaleString('en-US')} of them`
  const itemClauses: string[] = []
  if (build.starterItems.length) itemClauses.push(`starts ${names(build.starterItems)}`)
  if (build.coreItems.length) itemClauses.push(`completes ${names(build.coreItems)} in that order`)
  if (build.boots) itemClauses.push(`takes ${build.boots.name}`)
  if (itemClauses.length) {
    // The `and` belongs to the join, not to any one clause: hard-coding it on
    // the boots clause produced a bare "— and takes Sorcerer's Shoes." whenever
    // the starter and core path were the missing halves. Oxford comma because
    // these clauses are long enough that the unpunctuated version misreads.
    const joined = itemClauses.length > 1
      ? `${itemClauses.slice(0, -1).join(', ')}, and ${itemClauses[itemClauses.length - 1]}`
      : itemClauses[0]
    sentences.push(`${opener} — ${joined}.`)
  }

  if (build.keystone) {
    // Built as two clauses joined by a comma rather than one chain of `and`s:
    // the primary tree already needs an `and` inside its own rune list, so
    // appending the secondary with another one produced "… and Treasure Hunter
    // and Sorcery secondary …", which reads as a single four-item list.
    let primary = `It runs ${build.keystone.name}`
    if (build.primaryStyle) primary += ` out of ${build.primaryStyle.name}`
    if (build.primaryRunes.length) primary += ` with ${names(build.primaryRunes)}`
    let secondary = ''
    if (build.secondaryStyle) {
      secondary = build.secondaryRunes.length
        ? `, and ${build.secondaryStyle.name} secondary for ${names(build.secondaryRunes)}`
        : `, and ${build.secondaryStyle.name} secondary`
    }
    sentences.push(`${primary}${secondary}.`)
  }

  if (build.skills.length) {
    const chain = build.skills.map(skill =>
      skill.name ? `${skill.key} (${skill.name})` : skill.key,
    )
    const [first, ...rest] = chain
    sentences.push(
      rest.length
        ? `Abilities are levelled ${first} first, then ${rest.join(', then ')}.`
        : `${first} is levelled first.`,
    )
  }

  if (build.summonerSpells.length) {
    sentences.push(`Summoner spells are ${names(build.summonerSpells)}.`)
  }

  const others = summary.buildCount - 1
  if (others > 0) {
    sentences.push(
      others === 1
        ? `One other build is played often enough ${lane || 'on this lane'} to be measured on its own.`
        : `${capitalise(spellSmallNumber(others))} other builds are played often enough ${lane || 'on this lane'} to be measured on their own.`,
    )
  }

  return sentences
}
