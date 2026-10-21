import type { Ref } from 'vue'

/**
 * The player's kits in champion select (#2205): every kit on our pick's lane,
 * offered beside TrueMain's builds — none picked at first, the draft's build
 * being the answer. The kit picked is kept for the game (the store's `active`
 * entry for the champion and lane), so the overlay follows it; a build picked
 * instead, or a new pick or lane, clears it. Reading another champion's build
 * and coming back (`pick` null meanwhile) keeps it.
 */
export function useDraftKits(pick: Ref<{ championId: number, position: string } | null>) {
  const { kitsFor, activeIdFor, setActive } = useKits()
  const run = useKitAction()

  const kits = computed(() => (pick.value ? kitsFor(pick.value.championId, pick.value.position) : []))
  const kitId = ref<string | null>(null)

  function keepForGame(id: string | null) {
    const current = pick.value
    if (!current || activeIdFor(current.championId, current.position) === id) return
    void run(() => setActive(current.championId, current.position, id), 'Could not keep the kit for the game')
  }

  let last: string | null = null
  watch(() => (pick.value ? `${pick.value.championId}:${pick.value.position}` : null), (key) => {
    if (!key || key === last) return
    last = key
    kitId.value = null
    keepForGame(null)
  }, { immediate: true })

  function pickKit(id: string | null) {
    kitId.value = id
    keepForGame(id)
  }

  return { kits, kitId, pickKit }
}
