import type { ChampionStaticListItem } from '~~/shared/types/static-data'

interface ChampionListResponse {
  data: Record<string, { id: string, key: string, name: string, image: { full: string } }>
}

async function resolveLatestPatch(): Promise<string> {
  const versions = await $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json')
  const latest = versions[0]
  if (!latest) {
    throw createError({ statusCode: 502, statusMessage: 'DDragon returned no versions' })
  }
  return latest
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
      .filter(item => Number.isFinite(item.championId))
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-champion-list',
    getKey: (patch: string) => patch,
  },
)

export default defineEventHandler(async (event): Promise<ChampionStaticListItem[]> => {
  const { patch } = getQuery(event) as { patch?: string }
  // Resolve outside the cached layer so a new Riot patch invalidates the
  // cache key naturally. resolveLatestPatch hits DDragon's versions.json,
  // which is itself cached by their CDN (~10 min Cache-Control).
  const resolved = patch && patch.length > 0 ? patch : await resolveLatestPatch()
  return loadChampionsForPatch(resolved)
})
