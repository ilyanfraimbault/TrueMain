import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticPerkData,
  StaticPerkStyleData,
} from '~~/shared/types/static-data'

/**
 * The app's side of the composables the twinned site components call, so the
 * components themselves stay verbatim copies. Each answers the same question
 * the site's does, for an app with no image server and no player pages.
 */

/**
 * Twin of `useBuildResolvers` in `web/app/composables/useBuildAssets.ts`:
 * build ids to the icon objects the `GameTooltip*` components render.
 */
export function useBuildResolvers(
  runeTree: MaybeRefOrGetter<RuneTreeResponse | null | undefined>,
  itemsMap: MaybeRefOrGetter<Record<number, StaticItemData> | undefined>,
) {
  function perk(id: number | null | undefined): StaticPerkData | null {
    return id != null ? toValue(runeTree)?.perks?.[id] ?? null : null
  }
  function perkStyle(id: number | null | undefined): StaticPerkStyleData | null {
    return id != null ? toValue(runeTree)?.perkStyles?.[id] ?? null : null
  }
  function item(id: number | null | undefined): StaticItemData | null {
    return id != null ? toValue(itemsMap)?.[id] ?? null : null
  }
  return { perk, perkStyle, item }
}

/**
 * The site's `useChampionSlugs`, answered with the app's routes: a champion
 * opens its builds page here. The app has no player-scoped champion page, so a
 * true main's champion opens the champion's own page.
 */
export function useChampionSlugs() {
  const pathFor = (championId: number) => `/champions/${championId}`
  return {
    pathFor,
    truemainPathFor: (_nameTag: string, championId: number) => pathFor(championId),
  }
}

/** The site routes icons through its image server; the app uses them as given. */
export function useCanonicalIcon() {
  return (src: string | null | undefined) => src ?? undefined
}

/** Every champion as the site's static list item — what `LeaderboardRow` resolves names and icons from. */
export function useChampionsById() {
  const { champions, portraitOf } = useChampionStatics()
  return computed(() => new Map<number, ChampionStaticListItem>(
    [...champions.value.values()].map(champion => [champion.id, {
      championId: champion.id,
      name: champion.name,
      iconUrl: portraitOf(champion.id) ?? '',
    }]),
  ))
}
