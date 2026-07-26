import type { StaticItemData } from '~~/shared/types/static-data'
import { normalizeDataDragonPatch } from '~~/shared/utils/ddragon'

interface ItemListResponse {
  data: Record<string, {
    name: string
    image: { full: string }
    gold?: { total?: number, purchasable?: boolean }
    inStore?: boolean
    plaintext?: string
    description?: string
    tags?: string[]
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
const loadItemsForPatch = defineCachedFunction(
  async (patch: string): Promise<Record<number, StaticItemData>> => {
    const items = await $fetch<ItemListResponse>(
      `https://ddragon.leagueoflegends.com/cdn/${patch}/data/en_US/item.json`,
    )

    return Object.fromEntries(
      Object.entries(items.data).map(([id, item]) => [
        Number(id),
        {
          id: Number(id),
          name: item.name,
          iconUrl: `https://ddragon.leagueoflegends.com/cdn/${patch}/img/item/${item.image.full}`,
          totalGold: item.gold?.total ?? 0,
          purchasable: item.gold?.purchasable,
          inStore: item.inStore,
          plaintext: item.plaintext,
          description: item.description,
          tags: item.tags,
        } satisfies StaticItemData,
      ]),
    )
  },
  {
    maxAge: 60 * 60,
    name: 'ddragon-item-list',
    getKey: (patch: string) => patch,
  },
)

export default defineEventHandler(async (event): Promise<Record<number, StaticItemData>> => {
  const { patch } = getQuery(event) as { patch?: string }
  const resolved = normalizeDataDragonPatch(patch) ?? await resolveLatestPatch()
  return loadItemsForPatch(resolved)
})
