import type { LeaderboardRowResponse, LeaderboardTopChampion } from '#shared/types/leaderboard'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
} from '#shared/types/static-data'
import { truemainNameTag } from '#shared/utils/truemain-path'
import { leaderboardChampionIcon, leaderboardChampionName } from '#common/utils/leaderboard-row'

/** A row's signature champion, resolved for `LeaderboardChampionBuild`. */
export interface LeaderboardMainBuild {
  champion: LeaderboardTopChampion
  name: string
  iconUrl: string | null
  /** The player's slug: the cluster links to their page on the champion. */
  nameTag: string
  keystone: StaticPerkData | null
  secondaryStyle: StaticPerkStyleData | null
  firstItem: StaticItemData | null
}

/**
 * The champion lookups a truemains leaderboard row draws with — names, icons
 * and the signature champion's build cluster — shared by the `/truemains`
 * table and the champion page's rows (#2028).
 */
export function useLeaderboardChampions(
  championsById: MaybeRefOrGetter<Map<number, ChampionStaticListItem>>,
  runeTree: MaybeRefOrGetter<RuneTreeResponse | null>,
  itemsMap: MaybeRefOrGetter<Record<number, StaticItemData>>,
) {
  const { perk, perkStyle, item } = useBuildResolvers(runeTree, itemsMap)

  const championName = (id: number) => leaderboardChampionName(toValue(championsById), id)
  const championIcon = (id: number) => leaderboardChampionIcon(toValue(championsById), id)

  /** `LeaderboardChampionBuild`'s props for the row's signature champion; null without one. */
  function mainBuild(row: LeaderboardRowResponse): LeaderboardMainBuild | null {
    const champion = row.topChampions[0]
    if (!champion) return null
    return {
      champion,
      name: championName(champion.championId),
      iconUrl: championIcon(champion.championId),
      nameTag: truemainNameTag(row.identity.gameName, row.identity.tagLine),
      keystone: perk(champion.primaryKeystoneId),
      secondaryStyle: perkStyle(champion.secondaryStyleId),
      firstItem: item(champion.firstItemId),
    }
  }

  return { championName, championIcon, mainBuild }
}
