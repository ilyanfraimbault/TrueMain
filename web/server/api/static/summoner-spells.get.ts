import type { StaticSummonerSpellData } from '~~/shared/types/static-data'
import { getSummonerSpellImageUrl, normalizeDataDragonPatch } from '~~/shared/utils/ddragon'

interface SummonerListResponse {
  data: Record<string, {
    key: string
    name: string
    image: { full: string }
    description?: string
    cooldown?: number[]
    summonerLevel?: number
  }>
}

async function resolveLatestPatch(): Promise<string> {
  const versions = await $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json')
  const latest = versions[0]
  if (!latest) {
    throw createError({ statusCode: 502, statusMessage: 'DDragon returned no versions' })
  }
  return latest
}

// Cached on the resolved patch — see champions.get.ts for the rationale.
const loadSummonersForPatch = defineCachedFunction(
  async (patch: string): Promise<Record<number, StaticSummonerSpellData>> => {
    const spells = await $fetch<SummonerListResponse>(
      `https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/summoner.json`,
    )

    return Object.fromEntries(
      Object.values(spells.data).map(spell => [
        Number(spell.key),
        {
          id: Number(spell.key),
          name: spell.name,
          iconUrl: getSummonerSpellImageUrl(spell.image.full, patch) ?? '',
          description: spell.description,
          cooldown: spell.cooldown?.[0],
          summonerLevel: spell.summonerLevel,
        } satisfies StaticSummonerSpellData,
      ]),
    )
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-summoner-list',
    getKey: (patch: string) => patch,
  },
)

export default defineEventHandler(async (event): Promise<Record<number, StaticSummonerSpellData>> => {
  const { patch } = getQuery(event) as { patch?: string }
  const resolved = normalizeDataDragonPatch(patch) ?? await resolveLatestPatch()
  return loadSummonersForPatch(resolved)
})
