import type { Ref } from 'vue'
import type { ChampionItemContextAxis } from '#shared/types/item-context'
import type { GameState } from '~/types/game'
import type { NextItemCandidate, NextItemReason } from '~/utils/next-item'
import { formatPercentage } from '#shared/utils/ddragon'
import { itemContextAxisPhrase } from '#shared/utils/item-context'
import { buildPath } from '~/utils/build-path'
import { builtAxisPhrase } from '~/utils/built-reasons'
import { goldToComplete, starterCost } from '~/utils/next-item'

/**
 * What the next-item panel shows (#1751), shared by the game page's panel and
 * the overlay's (#1747) so the two can never disagree: the item the
 * champion's mains complete next from where this build stands, why when a
 * situation moved it, its build path — the components in the order the mains
 * buy them, and which the gold in hand buys now (#2183) — the runners-up and the boots —
 * and, until one is bought, the starter. When the build order (#2101) says the
 * boots are due, they are that item.
 *
 * With an active kit on our champion and lane (#2185) the kit answers instead:
 * its next step, its row and its starter, with no share, reason or runner-up —
 * those are the mains', not the player's. Once the kit has nothing left, the
 * mains' answer takes over again and `kitDone` says so.
 */
export function useNextItemPanel(game: Ref<GameState>) {
  const { answer, pending, failed, order: completed } = useNextItem(game)
  const { starter: mainsStarter } = useStarter(game)
  const { kit, step: kitStep, row: kitRow, starter: kitStarter } = useKitStep(game, completed)
  const { items: statics } = useStaticData()

  const me = computed(() => game.value.players.find(player => player.isMe) ?? null)
  const held = computed(() => me.value?.items.map(item => item.itemId) ?? [])

  /** The kit is the answer: it has a step left. */
  const fromKit = computed(() => kitStep.value !== null)
  const kitDone = computed(() => kit.value !== null && kitStep.value === null)

  const build = computed(() => (fromKit.value ? null : answer.value?.build ?? null))
  /** The build as one row (#2101), the boots at the step the mains complete them at — or the kit's own. */
  const order = computed(() => (fromKit.value ? kitRow.value : answer.value?.order ?? []))
  /** The boots are the step to complete now: they take the panel's main slot. */
  const bootsDue = computed(() => order.value.find(step => step.state === 'Next')?.isBoots ?? false)
  const decision = computed(() => (bootsDue.value ? answer.value?.boots ?? null : answer.value?.build ?? null))
  const top = computed<NextItemCandidate | null>(() => {
    const step = kitStep.value
    if (step) return { itemId: step.itemId, share: 0, baseShare: 0, games: 0, wins: 0, reasons: [] }
    return decision.value?.candidates[0] ?? null
  })
  const runnersUp = computed(() => (fromKit.value ? [] : decision.value?.candidates.slice(1, 3) ?? []))
  // Once the row places them, the boots need no column of their own.
  const boots = computed(() => (order.value.some(step => step.isBoots) ? [] : answer.value?.boots?.candidates.slice(0, 3) ?? []))
  const starter = computed(() => (kit.value ? kitStarter.value : mainsStarter.value))

  const name = (itemId: number) => statics.value[itemId]?.name ?? `Item ${itemId}`
  const percent = (share: number) => formatPercentage(share, 0)

  /**
   * The strongest situation behind an item, in the site's own wording — or,
   * for what the enemies have built (#1750), the app's; nothing when no
   * situation moved it.
   */
  function reason(candidate: NextItemCandidate | null) {
    const strongest: NextItemReason | undefined = candidate?.reasons[0]
    if (!strongest) return null
    return itemContextAxisPhrase({ axis: strongest.axis, bucket: strongest.bucket } as ChampionItemContextAxis)
      ?? builtAxisPhrase(strongest.axis, strongest.bucket)
  }
  const why = computed(() => reason(top.value))

  const remaining = computed(() => (top.value ? goldToComplete(top.value.itemId, held.value, statics.value) : 0))
  const path = computed(() => (top.value ? buildPath(top.value.itemId, top.value.route, held.value, game.value.gold, statics.value) : null))
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

  return { answer, pending, statics, build, order, bootsDue, top, runnersUp, boots, why, remaining, path, state, name, percent, reason, affordable, starter, starterGold, kit, fromKit, kitDone }
}
