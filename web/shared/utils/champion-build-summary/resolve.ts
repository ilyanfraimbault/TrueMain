import type { ChampionBuildSummary, ChampionBuildSummaryBuild, SummaryEntity } from '../../types/champion-build-summary'
import type { ChampionResponse } from '../../types/champions'
import type { ChampionStaticData, RuneTreeResponse, StaticItemData, StaticSummonerSpellData } from '../../types/static-data'

// Resolution half of the build summary: the champion response and the static
// payloads turned into the named entities the sentences are written from.

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
