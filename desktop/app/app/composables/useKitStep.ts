import type { Ref } from 'vue'
import type { BuildItemSet } from '~/types/build'
import type { GameState } from '~/types/game'
import { kitOrder, nextKitStep } from '~/utils/kit-tree'
import { starterBought } from '~/utils/next-item'

/**
 * Where the player's active kit (#2185) stands in the running game: the kit
 * for our champion and lane, its next step from the items held — the branch
 * they follow, its first child where it splits — its row for the build order,
 * and its starter until one is bought. `step` is `null` once the kit has
 * nothing left, and the next-item panel then falls back to TrueMain's answer.
 */
export function useKitStep(game: Ref<GameState>, completed: Ref<number[]>) {
  const { champions } = useChampionStatics()
  const { activeKit } = useKits()
  const { items } = useStaticData()

  const me = computed(() => game.value.players.find(player => player.isMe) ?? null)
  const held = computed(() => me.value?.items.map(item => item.itemId) ?? [])

  const championId = computed(() => {
    const alias = me.value?.champion.toLowerCase()
    if (!alias) return null
    for (const champion of champions.value.values()) if (champion.alias.toLowerCase() === alias) return champion.id
    return null
  })

  const kit = computed(() => {
    const position = me.value?.position
    return championId.value && position ? activeKit(championId.value, position) : null
  })

  const step = computed(() => (kit.value ? nextKitStep(kit.value.tree, kit.value.boots, completed.value, held.value) : null))
  const row = computed(() => (kit.value ? kitOrder(kit.value.tree, kit.value.boots, completed.value, held.value) : []))

  const starter = computed<BuildItemSet | null>(() => {
    const basket = kit.value?.starter ?? []
    if (basket.length === 0 || starterBought(held.value, items.value)) return null
    return { itemIds: basket, games: 0, pickRate: 0, winRate: 0 }
  })

  return { kit, step, row, starter }
}
