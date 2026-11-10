import type { ChampionItemContextResponse } from '#shared/contract/api'
import type { ItemSetRefusal } from '~/generated/shell'
import type { BuildOption } from '~/types/build'
import { hasItems, itemSetBuild, situationalFromContext, situationalFromTree } from '~/utils/item-set'
import { shell } from '~/utils/shell'

/** A build with nothing to write, refused here as the shell would refuse it. */
export const EMPTY_BUILD: ItemSetRefusal = 'empty-build'

/** The build the button imports, and whose it is. */
export interface ItemSetSubject {
  championId: number
  champion: string
  position: string
  build: BuildOption
}

/**
 * The item set import button's one call (#1908): the build on screen written
 * into the client as the one TrueMain item set, replacing the previous one.
 * Only ever run on a click — the shell's write path is built on that rule
 * (`lcu::item_sets`).
 *
 * The situational block needs the item context, asked for here, on the click,
 * rather than for every build the view shows.
 */
export function useItemSetImport() {
  const { state } = useLcuState()
  const { items } = useStaticData()
  const toast = useToast()
  const pending = ref(false)
  const done = ref(false)
  let doneTimer: ReturnType<typeof setTimeout> | undefined

  /** The client has to be there to take the set. */
  const available = computed(() => state.value.connected)

  /** The situational items: the slice's verdicts, else the tree's other branches. */
  async function situational(subject: ItemSetSubject): Promise<number[]> {
    const path = subject.build.core.itemPath?.itemIds ?? []
    try {
      const context = await apiGet<ChampionItemContextResponse>(`/champions/${subject.championId}/item-context`, { position: subject.position })
      const fromContext = situationalFromContext(context.items, path)
      if (fromContext.length > 0) return fromContext
    }
    catch {
      // No context is not a failed import: the tree still says what else was built.
    }
    return situationalFromTree(subject.build.buildTree, path)
  }

  async function importItemSet(subject: ItemSetSubject) {
    if (pending.value) return
    pending.value = true
    try {
      const build = itemSetBuild(subject.build.core, await situational(subject), itemId => itemId in items.value)
      if (!hasItems(build)) throw new Error(EMPTY_BUILD)
      await shell.importItemSet(subject.championId, subject.champion, build)
      done.value = true
      clearTimeout(doneTimer)
      doneTimer = setTimeout(() => (done.value = false), 2500)
      toast.add({
        title: 'Items imported',
        description: `The TrueMain item set is in the shop for ${subject.champion}.`,
        icon: 'i-lucide-check',
        color: 'success',
      })
    }
    catch (error) {
      toast.add(itemSetFailureToast(error instanceof Error ? error.message : String(error)))
    }
    finally {
      pending.value = false
    }
  }

  onScopeDispose(() => clearTimeout(doneTimer))

  return { available, pending, done, importItemSet }
}

/** The shell's refusals (`itemsets.rs`), in the player's words. */
const REFUSALS: Record<ItemSetRefusal, ReturnType<typeof itemSetFailureToast>> = {
  'read-failed': {
    title: 'Could not read your item sets',
    description: 'Nothing was written. Try again once the client has finished loading.',
    icon: 'i-lucide-circle-x',
    color: 'error',
  },
  'sets-changed': {
    title: 'Check your item sets',
    description: 'The TrueMain set was written, but your own sets no longer read the same in the client.',
    icon: 'i-lucide-triangle-alert',
    color: 'warning',
  },
  'empty-build': {
    title: 'Nothing to import',
    description: 'This build has no item to put in a set.',
    icon: 'i-lucide-triangle-alert',
    color: 'warning',
  },
}

/** How a failed import is told, shared with a kit's (`useKitImport`). */
export function itemSetFailureToast(message: string): { title: string, description: string, icon: string, color: 'error' | 'warning' } {
  return REFUSALS[message as ItemSetRefusal] ?? {
    title: 'Could not import the items',
    description: message,
    icon: 'i-lucide-circle-x',
    color: 'error',
  }
}
