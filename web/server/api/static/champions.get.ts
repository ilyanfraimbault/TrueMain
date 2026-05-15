import type { ChampionStaticListItem } from '~~/shared/types/static-data'

interface ChampionListResponse {
  data: Record<string, { id: string, key: string, name: string, image: { full: string } }>
}

async function resolveLatestPatch(): Promise<string | null> {
  const versions = await $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json').catch(() => [])
  return versions[0] ?? null
}

export default defineCachedEventHandler(
  async (event): Promise<ChampionStaticListItem[]> => {
    const { patch } = getQuery(event) as { patch?: string }
    const resolved = patch && patch.length > 0 ? patch : await resolveLatestPatch()
    if (!resolved) return []

    const champs = await $fetch<ChampionListResponse>(
      `https://ddragon.leagueoflegends.com/cdn/${resolved}/data/en_US/champion.json`,
    ).catch(() => ({ data: {} } as ChampionListResponse))

    return Object.values(champs.data)
      .map(champ => ({
        championId: Number(champ.key),
        name: champ.name,
        iconUrl: `https://ddragon.leagueoflegends.com/cdn/${resolved}/img/champion/${champ.image.full}`,
      }))
      .filter(item => Number.isFinite(item.championId))
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-champion-list',
    getKey: (event) => {
      const { patch } = getQuery(event) as { patch?: string }
      return `champion-list-${patch ?? ''}`
    },
  },
)
