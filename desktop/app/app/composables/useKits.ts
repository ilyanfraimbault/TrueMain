import type { Kit, KitStore } from '~/types/kit'
import { activeKey, cloneKit, EMPTY_STORE, freeName, newKitId, readStore } from '~/utils/kit'
import { follow, shell, shellEvents } from '~/utils/shell'

async function readBack(): Promise<KitStore> {
  return readStore(await shell.kitsGet())
}

async function write(store: KitStore): Promise<KitStore> {
  return readStore(await shell.kitsSet(store))
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
        await follow(shellEvents.kitsChanged, readBack, next => (store.value = readStore(next)))
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

  /** The kits offered on a champion's position. */
  const kitsFor = (championId: number, position: string) =>
    store.value.kits.filter(kit => kit.championId === championId && kit.positions.includes(position))

  const activeIdFor = (championId: number, position: string) => store.value.active[activeKey(championId, position)] ?? null

  const activeKit = (championId: number, position: string): Kit | null => {
    const id = activeIdFor(championId, position)
    return id ? store.value.kits.find(kit => kit.id === id) ?? null : null
  }

  /** Add the kit, or replace the one with its id; a position it no longer serves forgets it as active there. */
  function save(kit: Kit) {
    const saved = { ...kit, updatedAt: new Date().toISOString() }
    const exists = store.value.kits.some(other => other.id === kit.id)
    const kits = exists ? store.value.kits.map(other => (other.id === kit.id ? saved : other)) : [...store.value.kits, saved]
    const served = new Set(kit.positions.map(position => activeKey(kit.championId, position)))
    const active = Object.fromEntries(Object.entries(store.value.active).filter(([key, id]) => id !== kit.id || served.has(key)))
    return commit({ kits, active })
  }

  function remove(id: string) {
    const active = Object.fromEntries(Object.entries(store.value.active).filter(([, kitId]) => kitId !== id))
    return commit({ kits: store.value.kits.filter(kit => kit.id !== id), active })
  }

  /** A copy beside it, under a free name; the copy is not active. */
  async function duplicate(id: string): Promise<Kit | null> {
    const kit = store.value.kits.find(other => other.id === id)
    if (!kit) return null
    const taken = store.value.kits.filter(other => other.championId === kit.championId).map(other => other.name)
    const copy: Kit = { ...cloneKit(kit), id: newKitId(), name: freeName(`${kit.name} copy`, taken) }
    await save(copy)
    return copy
  }

  function rename(id: string, name: string) {
    const kit = store.value.kits.find(other => other.id === id)
    return kit && name.trim() ? save({ ...kit, name: name.trim() }) : Promise.resolve()
  }

  /**
   * The kit picked in champion select on a champion's position — the one the
   * overlay then follows in game; `null` when a TrueMain build was picked instead.
   */
  function setActive(championId: number, positions: string | string[], id: string | null) {
    const keys = new Set((Array.isArray(positions) ? positions : [positions]).map(position => activeKey(championId, position)))
    const others = Object.entries(store.value.active).filter(([other]) => !keys.has(other))
    const active = Object.fromEntries(id ? [...others, ...[...keys].map(key => [key, id])] : others)
    return commit({ ...store.value, active })
  }


  return { store, error, kitsFor, activeIdFor, activeKit, save, remove, duplicate, rename, setActive }
}
