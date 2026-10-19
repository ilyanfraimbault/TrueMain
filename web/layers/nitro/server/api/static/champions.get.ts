import type { ChampionStaticListItem } from '../../../../../shared/types/static-data'
import { isLiveChampionId } from '../../../../../shared/utils/ddragon'
import { normalizeRequestedPatch, resolveDDragonVersion } from '../../utils/ddragon-patch'

/**
 * The DDragon champion list, which both apps read and must answer identically —
 * one copy in `web/layers/nitro` (#1625; the two copies it replaced had drifted,
 * #1226). Open on the site; the admin puts it behind the operator session with
 * its own middleware (`admin/server/middleware/static-session.ts`), which runs
 * before this handler.
 */

interface ChampionListResponse {
  data: Record<string, { id: string, key: string, name: string, image: { full: string } }>
}

// Cached on the resolved patch — not on the raw query param. Without this,
// "?patch=" (the no-patch case) would cache against a fixed key and keep
// serving the previous patch's data after a new patch ships on DDragon.
const loadChampionsForPatch = defineCachedFunction(
  async (patch: string): Promise<ChampionStaticListItem[]> => {
    const champs = await $fetch<ChampionListResponse>(
      `https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/champion.json`,
    )

    return Object.values(champs.data)
      .map(champ => ({
        championId: Number(champ.key),
        name: champ.name,
        iconUrl: `https://ddragon.leagueoflegends.com/cdn/${patch}/img/champion/${champ.image.full}`,
      }))
      .filter(item => isLiveChampionId(item.championId))
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-champion-list',
    getKey: (patch: string) => patch,
  },
)

export default defineEventHandler(async (event): Promise<ChampionStaticListItem[]> => {
  const { patch } = getQuery(event) as { patch?: string }
  // Backend scopes expose patches in the short "16.5" form; DDragon CDN paths
  // need "16.5.1". Normalize (and validate) here so a caller can pass the patch
  // straight from a champion summary, then resolve it against the versions
  // DDragon actually publishes — none supplied means the latest, and a patch
  // Riot has shipped but DDragon has not falls back rather than 403ing (#1693).
  const resolved = await resolveDDragonVersion(normalizeRequestedPatch(patch))
  return loadChampionsForPatch(resolved)
})
