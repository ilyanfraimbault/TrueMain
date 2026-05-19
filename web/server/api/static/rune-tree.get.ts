import type { RuneTreeResponse, RuneTreeStyle } from '~~/shared/types/static-data'
import {
  buildPerkMap,
  buildPerkStyleMap,
  COMMUNITY_DRAGON_PREFIX,
  rewriteCdragonAsset,
  type CdragonPerkRow,
  type CdragonPerkStyleRow,
} from '~~/server/utils/ddragon-loader'

interface PerkStylesResponse {
  styles: Array<CdragonPerkStyleRow & {
    slots: Array<{ type: string, perks: number[] }>
  }>
}

const loadRuneTree = defineCachedFunction(
  async (): Promise<RuneTreeResponse> => {
    // Let any CommunityDragon failure bubble up — we'd rather return 502 once
    // and let the next request retry than cache an empty `RuneTreeResponse`
    // for 1h on a transient outage. Same trade-off as champions.get.ts.
    const [perks, perkStyles] = await Promise.all([
      $fetch<CdragonPerkRow[]>(`${COMMUNITY_DRAGON_PREFIX}/v1/perks.json`),
      $fetch<PerkStylesResponse>(`${COMMUNITY_DRAGON_PREFIX}/v1/perkstyles.json`),
    ]).catch((error) => {
      throw createError({
        statusCode: 502,
        statusMessage: 'CommunityDragon rune tree fetch failed',
        cause: error,
      })
    })

    const perkMap = buildPerkMap(perks)
    const perkStyleMap = buildPerkStyleMap(perkStyles.styles)

    // CommunityDragon emits 7 slots per style: 0 = keystone, 1-3 = regular
    // sub-rows, 4-6 = stat shards (same triplets for every style, so we read
    // them once from any style).
    const styles: RuneTreeStyle[] = perkStyles.styles.map(style => ({
      styleId: style.id,
      name: style.name,
      iconUrl: rewriteCdragonAsset(style.iconPath),
      keystones: style.slots[0]?.perks ?? [],
      subRows: style.slots.slice(1, 4).map(slot => slot.perks),
    }))

    const firstStyle = perkStyles.styles[0]
    const shardSlots = firstStyle ? firstStyle.slots.slice(4, 7).map(slot => slot.perks) : []

    return {
      styles,
      perks: perkMap,
      perkStyles: perkStyleMap,
      shardSlots,
    }
  },
  {
    maxAge: 60 * 60,
    name: 'cdragon-rune-tree',
    getKey: () => 'rune-tree',
  },
)

export default defineEventHandler(async (): Promise<RuneTreeResponse> => {
  return loadRuneTree()
})
