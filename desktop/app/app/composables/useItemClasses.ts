import type { ItemClassMap } from '~/utils/kit-items'

interface ItemClassesResponse {
  patch: string
  items: { itemId: number, classes: string[] }[]
}

/**
 * The in-game shop's class of every item (Fighter, Marksman, …), read from the
 * API once per patch — the game data it comes from is too large for the app to
 * read itself. Empty until it answers, and after a failure, which the next
 * patch read retries: the item picker then simply offers no class filter.
 */
export function useItemClasses() {
  const { patch } = useChampionStatics()
  const classes = useState<ItemClassMap>('item-classes', () => ({}))
  const loadedFor = useState<string>('item-classes-patch', () => '')

  async function load(version: string) {
    if (!version || loadedFor.value === version) return
    loadedFor.value = version
    try {
      const answer = await apiGet<ItemClassesResponse>('/items/classes', { patch: version }, { background: true })
      classes.value = Object.fromEntries(answer.items.map(item => [item.itemId, item.classes]))
    }
    catch {
      loadedFor.value = ''
    }
  }

  watch(patch, load, { immediate: true })

  return { classes }
}
