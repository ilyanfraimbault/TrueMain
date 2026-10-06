import type { ChampionItemContextResponse } from '#shared/types/item-context'
import type { BuildOption } from '~/types/build'
import { hasItems, itemSetBuild, situationalFromContext, situationalFromTree } from '~/utils/item-set'

/** What the shell answers when it could not read the client's list (`itemsets.rs`). */
const READ_FAILED = 'read-failed'
/** What the shell answers when the player's own sets no longer read the same. */
const SETS_CHANGED = 'sets-changed'
/** What the shell answers for a build with no item to write. */
const EMPTY_BUILD = 'empty-build'

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
      if (insideTauri()) {
        const { invoke } = await import('@tauri-apps/api/core')
        await invoke('import_item_set', { championId: subject.championId, champion: subject.champion, build })
      }
      done.value = true
      clearTimeout(doneTimer)
      doneTimer = setTimeout(() => (done.value = false), 2500)
      toast.add({
        title: 'Items imported',
        description: insideTauri() ? `The TrueMain item set is in the shop for ${subject.champion}.` : 'Dev server: nothing was sent to a client.',
        icon: 'i-lucide-check',
        color: 'success',
      })
    }
    catch (error) {
      toast.add(failureToast(error instanceof Error ? error.message : String(error)))
    }
    finally {
      pending.value = false
    }
  }

  onScopeDispose(() => clearTimeout(doneTimer))

  return { available, pending, done, importItemSet }
}

function failureToast(message: string) {
  switch (message) {
    case READ_FAILED:
      return {
        title: 'Could not read your item sets',
        description: 'Nothing was written. Try again once the client has finished loading.',
        icon: 'i-lucide-circle-x',
        color: 'error' as const,
      }
    case SETS_CHANGED:
      return {
        title: 'Check your item sets',
        description: 'The TrueMain set was written, but your own sets no longer read the same in the client.',
        icon: 'i-lucide-triangle-alert',
        color: 'warning' as const,
      }
    case EMPTY_BUILD:
      return {
        title: 'Nothing to import',
        description: 'This build has no item to put in a set.',
        icon: 'i-lucide-triangle-alert',
        color: 'warning' as const,
      }
    default:
      return {
        title: 'Could not import the items',
        description: message,
        icon: 'i-lucide-circle-x',
        color: 'error' as const,
      }
  }
}
