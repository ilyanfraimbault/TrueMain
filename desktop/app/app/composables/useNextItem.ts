import type { Ref } from 'vue'
import type { GameState } from '~/types/game'
import type { NextItemResponse } from '~/utils/next-item'
import { nextItemRequest, nextItemSignature, noteNewItems } from '~/utils/next-item'

/** A purchase often lands as several readings (component, then the combine); wait for the inventory to settle. */
const SETTLE_MS = 600

/**
 * The next item to complete in the running game (#1751), from the mains'
 * measured choices (`POST /champions/{id}/next-item`, #1749).
 *
 * Asked again whenever an item changes hands anywhere on the board — never on
 * a timer: the answer depends on our build and on what the game looks like,
 * and nothing else moves it. A request still out when the next one is due is
 * dropped rather than waited for.
 */
export function useNextItem(game: Ref<GameState | null>) {
  const { champions } = useChampionStatics()

  const answer = ref<NextItemResponse | null>(null)
  const pending = ref(false)
  const failed = ref(false)

  /** The order our items appeared in, kept for this game only. */
  const order = useState<number[]>('next-item-order', () => [])
  /** The game the order belongs to: who we are, on what, and the latest game time it saw. */
  const orderOf = useState<{ owner: string, gameTime: number } | null>('next-item-order-of', () => null)

  const idByAlias = computed(() => {
    const map = new Map<string, number>()
    for (const champion of champions.value.values()) map.set(champion.alias.toLowerCase(), champion.id)
    return map
  })
  const championIdOf = (alias: string) => idByAlias.value.get(alias.toLowerCase()) ?? null

  // A new game starts a new order. The same player on the same champion twice
  // in a row is the common case, so the owner alone cannot tell two games
  // apart: the game ending (no state) or its clock going back both can.
  watch(game, (current) => {
    const me = current?.players.find(player => player.isMe)
    if (!current || !me) {
      orderOf.value = null
      order.value = []
      return
    }
    const owner = `${me.riotId}|${me.champion}`
    if (orderOf.value?.owner !== owner || current.gameTime < orderOf.value.gameTime) order.value = []
    orderOf.value = { owner, gameTime: current.gameTime }
    order.value = noteNewItems(order.value, me.items.map(item => item.itemId))
  }, { immediate: true, deep: true })

  let generation = 0
  let settle: ReturnType<typeof setTimeout> | undefined
  let controller: AbortController | undefined

  async function ask() {
    const current = game.value
    const request = current ? nextItemRequest(current, order.value, championIdOf) : null
    if (!request) {
      answer.value = null
      return
    }
    const mine = ++generation
    controller?.abort()
    controller = new AbortController()
    pending.value = true
    try {
      const response = await apiPost<NextItemResponse>(`/champions/${request.championId}/next-item`, request.body, {}, controller.signal)
      if (mine !== generation) return
      answer.value = response
      failed.value = false
    }
    catch {
      if (mine !== generation) return
      failed.value = true
    }
    finally {
      if (mine === generation) pending.value = false
    }
  }

  watch(
    () => (game.value ? `${nextItemSignature(game.value)}#${idByAlias.value.size}` : ''),
    (signature, previous) => {
      if (!signature || signature === previous) return
      clearTimeout(settle)
      settle = setTimeout(ask, previous ? SETTLE_MS : 0)
    },
    { immediate: true },
  )

  onScopeDispose(() => {
    clearTimeout(settle)
    controller?.abort()
  })

  return { answer, pending, failed }
}
