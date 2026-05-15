import type {
  RuneTreeResponse,
  RuneTreeStyle,
  StaticPerkData,
  StaticPerkStyleData,
} from '~~/shared/types/static-data'

const COMMUNITY_DRAGON_PREFIX
  = 'https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default'

interface PerksResponse extends Array<{
  id: number
  name: string
  iconPath: string
}> {}

interface PerkStylesResponse {
  styles: Array<{
    id: number
    name: string
    iconPath: string
    slots: Array<{ type: string, perks: number[] }>
  }>
}

function rewriteCdragonAsset(iconPath: string): string {
  if (!iconPath) return ''
  return iconPath.toLowerCase().replace(/^\/lol-game-data\/assets/, COMMUNITY_DRAGON_PREFIX)
}

const loadRuneTree = defineCachedFunction(
  async (): Promise<RuneTreeResponse> => {
    const [perks, perkStyles] = await Promise.all([
      $fetch<PerksResponse>(`${COMMUNITY_DRAGON_PREFIX}/v1/perks.json`),
      $fetch<PerkStylesResponse>(`${COMMUNITY_DRAGON_PREFIX}/v1/perkstyles.json`),
    ])

    const perkMap: Record<number, StaticPerkData> = Object.fromEntries(
      perks.map(perk => [perk.id, {
        id: perk.id,
        name: perk.name,
        iconUrl: rewriteCdragonAsset(perk.iconPath),
      }]),
    )

    const perkStyleMap: Record<number, StaticPerkStyleData> = Object.fromEntries(
      perkStyles.styles.map(style => [style.id, {
        id: style.id,
        name: style.name,
        iconUrl: rewriteCdragonAsset(style.iconPath),
      }]),
    )

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
    name: 'ddragon-rune-tree',
    getKey: () => 'rune-tree',
  },
)

export default defineEventHandler(async (): Promise<RuneTreeResponse> => {
  return loadRuneTree()
})
