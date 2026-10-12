import type { Ref } from 'vue'
import type { ChampionItemContextAxis } from '#shared/types/item-context'
import type { GameState } from '~/types/game'
import type { NextItemCandidate, NextItemReason } from '~/utils/next-item'
import { itemContextAxisPhrase } from '#shared/utils/item-context'
import { goldToComplete, starterCost, stepsToward } from '~/utils/next-item'

/**
 * What the next-item panel shows (#1751), shared by the game page's panel and
 * the overlay's (#1747) so the two can never disagree: the item the
 * champion's mains complete next from where this build stands, why when a
 * situation moved it, the purchases toward it, the runners-up and the boots —
 * and, until one is bought, the starter.
 */
export function useNextItemPanel(game: Ref<GameState>) {
  const { answer, pending, failed } = useNextItem(game)
  const { starter } = useStarter(game)
  const { items: statics } = useStaticData()

  const me = computed(() => game.value.players.find(player => player.isMe) ?? null)
  const held = computed(() => me.value?.items.map(item => item.itemId) ?? [])

  const build = computed(() => answer.value?.build ?? null)
  const top = computed(() => build.value?.candidates[0] ?? null)
  const runnersUp = computed(() => build.value?.candidates.slice(1, 3) ?? [])
  const boots = computed(() => answer.value?.boots?.candidates.slice(0, 3) ?? [])

  const name = (itemId: number) => statics.value[itemId]?.name ?? `Item ${itemId}`
  const percent = (share: number) => `${Math.round(share * 100)}%`

  /** The strongest situation behind an item, in the site's own wording; nothing when no situation moved it. */
  function reason(candidate: NextItemCandidate | null) {
    const strongest: NextItemReason | undefined = candidate?.reasons[0]
    if (!strongest) return null
    return itemContextAxisPhrase({ axis: strongest.axis, bucket: strongest.bucket } as ChampionItemContextAxis)
  }
  const why = computed(() => reason(top.value))

  const remaining = computed(() => (top.value ? goldToComplete(top.value.itemId, held.value, statics.value) : 0))
  const steps = computed(() => (top.value ? stepsToward(top.value.itemId, held.value, statics.value) : []))
  const affordable = (cost: number) => cost <= game.value.gold
  const starterGold = computed(() => (starter.value ? starterCost(starter.value.itemIds, statics.value) : 0))

  const state = computed(() => {
    if (top.value) return 'ready'
    if (pending.value && !answer.value) return 'loading'
    if (failed.value && !answer.value) return 'offline'
    if (answer.value && !answer.value.patch) return 'unmeasured'
    if (answer.value && !answer.value.build) return 'complete'
    return 'loading'
  })

  return { answer, pending, statics, build, top, runnersUp, boots, why, remaining, steps, state, name, percent, reason, affordable, starter, starterGold }
}
