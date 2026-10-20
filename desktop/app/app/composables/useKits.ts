import type { Kit, KitStore } from '#shared/types/kit'
import { activeKey, EMPTY_STORE, freeName, newKitId, readStore } from '~/utils/kit'

/** Where the dev server keeps kits, with no shell to keep them. */
const BROWSER_KEY = 'truemain:kits:v1'

/** What the shell emits when the kits change, for every window — the overlay's panels included. */
const CHANGED_EVENT = 'kits://changed'

async function readBack(): Promise<KitStore> {
  if (insideTauri()) {
    const { invoke } = await import('@tauri-apps/api/core')
    return readStore(await invoke('kits_get'))
  }
  try {
    return readStore(JSON.parse(localStorage.getItem(BROWSER_KEY) ?? 'null'))
  }
  catch {
    return { ...EMPTY_STORE }
  }
}

async function write(store: KitStore): Promise<KitStore> {
  if (insideTauri()) {
    const { invoke } = await import('@tauri-apps/api/core')
    return readStore(await invoke('kits_set', { store }))
  }
  localStorage.setItem(BROWSER_KEY, JSON.stringify(store))
  return store
}

/**
 * The player's kits (#2185), kept by the shell in its own file
 * (`shell_state::kits`), and in the dev server's `localStorage` so the editor
 * can be worked on in a browser. One store for every component of a window,
 * read once and followed through the shell's event, so a kit saved in the app
 * reaches the overlay's panels during a game.
 *
 * Every change writes the whole store and takes back what the shell kept: the
 * shell is the authority on what a valid store is.
 */
export function useKits() {
  const store = useState<KitStore>('kits', () => ({ ...EMPTY_STORE }))
  const loaded = useState<boolean>('kits-loaded', () => false)
  const error = useState<string | null>('kits-error', () => null)

  if (!loaded.value && typeof window !== 'undefined') {
    loaded.value = true
    void (async () => {
      try {
        store.value = await readBack()
        if (insideTauri()) {
          const { listen } = await import('@tauri-apps/api/event')
          await listen<unknown>(CHANGED_EVENT, (event) => {
            store.value = readStore(event.payload)
          })
        }
      }
      catch (cause) {
        error.value = String(cause)
      }
    })()
  }

  async function commit(next: KitStore) {
    const before = store.value
    store.value = next
    try {
      store.value = await write(next)
      error.value = null
    }
    catch (cause) {
      store.value = before
      error.value = String(cause)
      throw cause
    }
  }

  const kitsFor = (championId: number, position: string) =>
    store.value.kits.filter(kit => kit.championId === championId && kit.position === position)

  const activeIdFor = (championId: number, position: string) => store.value.active[activeKey(championId, position)] ?? null

  const activeKit = (championId: number, position: string): Kit | null => {
    const id = activeIdFor(championId, position)
    return id ? store.value.kits.find(kit => kit.id === id) ?? null : null
  }

  /** Add the kit, or replace the one with its id. */
  function save(kit: Kit) {
    const saved = { ...kit, updatedAt: new Date().toISOString() }
    const exists = store.value.kits.some(other => other.id === kit.id)
    const kits = exists ? store.value.kits.map(other => (other.id === kit.id ? saved : other)) : [...store.value.kits, saved]
    return commit({ ...store.value, kits })
  }

  function remove(id: string) {
    const active = Object.fromEntries(Object.entries(store.value.active).filter(([, kitId]) => kitId !== id))
    return commit({ kits: store.value.kits.filter(kit => kit.id !== id), active })
  }

  /** A copy beside it, under a free name; the copy is not active. */
  async function duplicate(id: string): Promise<Kit | null> {
    const kit = store.value.kits.find(other => other.id === id)
    if (!kit) return null
    const taken = kitsFor(kit.championId, kit.position).map(other => other.name)
    const copy: Kit = { ...JSON.parse(JSON.stringify(kit)) as Kit, id: newKitId(), name: freeName(`${kit.name} copy`, taken) }
    await save(copy)
    return copy
  }

  function rename(id: string, name: string) {
    const kit = store.value.kits.find(other => other.id === id)
    return kit && name.trim() ? save({ ...kit, name: name.trim() }) : Promise.resolve()
  }

  /** The kit champion select and the overlay use on its champion and position; `null` goes back to TrueMain's. */
  function setActive(championId: number, position: string, id: string | null) {
    const key = activeKey(championId, position)
    const others = Object.entries(store.value.active).filter(([other]) => other !== key)
    const active = Object.fromEntries(id ? [...others, [key, id]] : others)
    return commit({ ...store.value, active })
  }

  return { store, error, kitsFor, activeIdFor, activeKit, save, remove, duplicate, rename, setActive }
}
