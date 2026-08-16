import type { ChampionSlugMap } from '~~/shared/types/static-data'
import { isLiveChampionId, toChampionSlug } from '~~/shared/utils/ddragon'
import { resolveLatestDDragonPatch } from '~~/server/utils/ddragon-patch'

/**
 * `championId → url slug` for every live champion (#1124).
 *
 * Deliberately its own endpoint rather than a field on `/api/static/champions`:
 * this map is loaded into app-wide state on **every** page (see
 * `plugins/champion-slugs.ts`), because every page builds champion links, so it
 * has to be the smallest thing that answers the question. The full list carries
 * names and CDN icon URLs and is ~20× larger; this is ~2.5 kB.
 *
 * Not patch-scoped, unlike its siblings. A champion's DDragon key never changes
 * — `Ahri` has been `Ahri` since 2011 — so a patch dimension would fragment the
 * cache and the client state for a value that is the same in every entry. The
 * latest patch is resolved only so a *newly released* champion appears without
 * a deploy.
 */
interface ChampionListResponse {
  data: Record<string, { id: string, key: string }>
}

const loadChampionSlugs = defineCachedFunction(
  async (patch: string): Promise<ChampionSlugMap> => {
    const champs = await $fetch<ChampionListResponse>(
      `https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/champion.json`,
    )

    const slugs: ChampionSlugMap = {}
    for (const champ of Object.values(champs.data)) {
      const championId = Number(champ.key)
      // Same guard as the champion list: alternate-mode entries share the
      // namespace and must never become linkable pages.
      if (isLiveChampionId(championId)) slugs[championId] = toChampionSlug(champ.id)
    }
    return slugs
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-champion-slugs',
    getKey: (patch: string) => patch,
  },
)

export default defineEventHandler(async (): Promise<ChampionSlugMap> => {
  return loadChampionSlugs(await resolveLatestDDragonPatch())
})
