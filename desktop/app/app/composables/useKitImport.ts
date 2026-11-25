import { isChampSelect } from '~/utils/gameflow'
import type { Kit } from '~/types/kit'
import { EMPTY_BUILD, itemSetFailureToast } from '~/composables/useItemSetImport'
import { kitItemSetBlocks } from '~/utils/item-set'
import { shell } from '~/utils/shell'

/** What the shell answers outside champion select (`lcu::Refusal::NotYourTurn`). */
const NOT_IN_CHAMP_SELECT = 'not-your-turn'

/**
 * A kit's two writes beyond the rune page (#2185), each the twin of a build's
 * import and only ever run on its click: the kit's tree as the one TrueMain
 * item set — one block per path, after the starter and the boots — and its
 * summoner spells into the player's champion select (`my-selection`). The
 * rune page goes through the build's own import (`useRuneImport`), into the
 * same TrueMain page.
 */
export function useKitImport() {
  const { state } = useLcuState()
  const { items } = useStaticData()
  const toast = useToast()
  const pending = ref<'items' | 'spells' | null>(null)

  const available = computed(() => state.value.connected)
  /** Spells can only be written while a champion select runs. */
  const inChampSelect = computed(() => state.value.connected && isChampSelect(state.value.phase))

  async function importItems(kit: Kit, champion: string) {
    if (pending.value) return
    pending.value = 'items'
    try {
      const name = (itemId: number) => items.value[itemId]?.name ?? `Item ${itemId}`
      const blocks = kitItemSetBlocks(kit, name, itemId => itemId in items.value)
      if (blocks.length === 0) throw new Error(EMPTY_BUILD)
      await shell.importKitItemSet(kit.championId, champion, blocks)
      toast.add({ title: 'Items imported', description: `Your kit is the TrueMain item set for ${champion}.`, icon: 'i-lucide-check', color: 'success' })
    }
    catch (error) {
      toast.add(itemSetFailureToast(error instanceof Error ? error.message : String(error)))
    }
    finally {
      pending.value = null
    }
  }

  async function importSpells(kit: Kit) {
    if (pending.value) return
    pending.value = 'spells'
    try {
      await shell.champSelectSpells(kit.spells[0], kit.spells[1])
      toast.add({ title: 'Spells set', description: 'Your kit\'s summoner spells are set in champion select.', icon: 'i-lucide-check', color: 'success' })
    }
    catch (error) {
      const message = String(error)
      toast.add(message === NOT_IN_CHAMP_SELECT
        ? { title: 'Not in champion select', description: 'Spells can be set once a champion select runs. Nothing was sent.', icon: 'i-lucide-triangle-alert', color: 'warning' }
        : { title: 'Could not set the spells', description: message, icon: 'i-lucide-circle-x', color: 'error' })
    }
    finally {
      pending.value = null
    }
  }

  return { available, inChampSelect, pending, importItems, importSpells }
}
