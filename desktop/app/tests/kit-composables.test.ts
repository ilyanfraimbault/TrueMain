import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, watch, type Ref } from 'vue'
import type { KitStore } from '#shared/types/kit'
import type { GameState } from '~/types/game'
import { item, kit } from './kit-fixtures'

/**
 * The kit composables against stand-ins for Nuxt's auto-imports and the shell:
 * `useKits` on the browser's store and on the shell's commands, the imports'
 * toasts, and the walk the next-item panel takes from a game.
 */

const shell = vi.hoisted(() => ({ invoke: vi.fn(), listeners: new Map<string, (event: { payload: unknown }) => void>() }))
vi.mock('@tauri-apps/api/core', () => ({ invoke: shell.invoke }))
vi.mock('@tauri-apps/api/event', () => ({
  listen: vi.fn(async (name: string, handler: (event: { payload: unknown }) => void) => {
    shell.listeners.set(name, handler)
    return () => {}
  }),
}))

let states: Map<string, Ref<unknown>>
let toasts: { title: string, description?: string, color?: string }[]
let tauri: boolean
const lcu = ref({ connected: true, phase: 'ChampSelect' })

const flush = () => new Promise(resolve => setTimeout(resolve, 0))

beforeEach(() => {
  vi.resetModules()
  states = new Map()
  toasts = []
  tauri = false
  shell.invoke.mockReset()
  shell.listeners.clear()
  localStorage.clear()
  lcu.value = { connected: true, phase: 'ChampSelect' }
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('onScopeDispose', () => {})
  vi.stubGlobal('useState', (key: string, init: () => unknown) => {
    if (!states.has(key)) states.set(key, ref(init()))
    return states.get(key)
  })
  vi.stubGlobal('insideTauri', () => tauri)
  vi.stubGlobal('useToast', () => ({ add: (toast: { title: string }) => toasts.push(toast) }))
  vi.stubGlobal('useLcuState', () => ({ state: lcu }))
  vi.stubGlobal('useStaticData', () => ({ items: ref({ 6655: item(6655), 3100: item(3100), 3089: item(3089), 4645: item(4645), 3020: item(3020), 1056: item(1056), 2003: item(2003, { tags: ['Consumable'] }) }) }))
  vi.stubGlobal('useChampionStatics', () => ({ champions: ref(new Map([[103, { id: 103, alias: 'Ahri' }]])) }))
})

afterEach(() => vi.unstubAllGlobals())

async function kitsStore() {
  const { useKits } = await import('~/composables/useKits')
  vi.stubGlobal('useKits', useKits)
  const kits = useKits()
  await flush()
  return kits
}

describe('useKits in a browser', () => {
  it('saves, marks, copies, renames and deletes, kept across a reload', async () => {
    const kits = await kitsStore()
    await kits.save(kit())
    await kits.setActive(103, 'MIDDLE', 'k1')
    expect(kits.activeKit(103, 'MIDDLE')?.name).toBe('Into tanks')

    const copy = await kits.duplicate('k1')
    expect(copy?.name).toBe('Into tanks copy')
    expect(kits.kitsFor(103, 'MIDDLE')).toHaveLength(2)
    expect(kits.activeIdFor(103, 'MIDDLE')).toBe('k1')
    expect(await kits.duplicate('missing')).toBeNull()

    await kits.rename(copy!.id, '  Into mages ')
    await kits.rename(copy!.id, '   ')
    expect(kits.kitsFor(103, 'MIDDLE').map(k => k.name)).toEqual(['Into tanks', 'Into mages'])

    await kits.remove('k1')
    expect(kits.activeKit(103, 'MIDDLE')).toBeNull()

    // A new window reads what the last one kept.
    states = new Map()
    const reloaded = await kitsStore()
    expect(reloaded.kitsFor(103, 'MIDDLE').map(k => k.name)).toEqual(['Into mages'])
  })

  it('goes back to TrueMain\'s build when the mark is cleared', async () => {
    const kits = await kitsStore()
    await kits.save(kit())
    await kits.setActive(103, 'MIDDLE', 'k1')
    await kits.setActive(103, 'MIDDLE', null)
    expect(kits.activeIdFor(103, 'MIDDLE')).toBeNull()
  })

  it('reads a broken store as no kits', async () => {
    localStorage.setItem('truemain:kits:v1', '{not json')
    const kits = await kitsStore()
    expect(kits.store.value).toEqual({ kits: [], active: {} })
  })
})

describe('useKits on several positions', () => {
  it('lists a kit on each of its positions, keeps it picked on several, and forgets a position it leaves', async () => {
    const kits = await kitsStore()
    const flex = kit({ id: 'f1', positions: ['TOP', 'MIDDLE'] })
    await kits.save(flex)
    expect(kits.kitsFor(103, 'TOP').map(k => k.id)).toEqual(['f1'])
    expect(kits.kitsFor(103, 'MIDDLE').map(k => k.id)).toEqual(['f1'])
    expect(kits.kitsFor(103, 'BOTTOM')).toEqual([])

    await kits.setActive(103, ['TOP', 'MIDDLE'], 'f1')
    expect([kits.activeIdFor(103, 'TOP'), kits.activeIdFor(103, 'MIDDLE')]).toEqual(['f1', 'f1'])
    await kits.save({ ...flex, positions: ['MIDDLE'] })
    expect(kits.store.value.active).toEqual({ '103:MIDDLE': 'f1' })
    await kits.setActive(103, 'MIDDLE', null)
    expect(kits.store.value.active).toEqual({})
  })
})

describe('useKits in the shell', () => {
  it('reads and writes through the shell, and follows its event', async () => {
    tauri = true
    const kept: KitStore = { kits: [kit()], active: { '103:MIDDLE': 'k1' } }
    shell.invoke.mockImplementation(async (command: string, args?: { store: KitStore }) => (command === 'kits_get' ? { kits: [], active: {} } : args?.store ?? kept))
    const kits = await kitsStore()
    expect(shell.invoke).toHaveBeenCalledWith('kits_get')

    await kits.save(kit())
    expect(shell.invoke).toHaveBeenCalledWith('kits_set', expect.objectContaining({ store: expect.any(Object) }))

    shell.listeners.get('kits://changed')!({ payload: kept })
    expect(kits.activeKit(103, 'MIDDLE')?.id).toBe('k1')
  })

  it('keeps the previous kits when the shell refuses the write', async () => {
    tauri = true
    shell.invoke.mockImplementation(async (command: string) => {
      if (command === 'kits_get') return { kits: [kit()], active: {} }
      throw new Error('disk full')
    })
    const kits = await kitsStore()
    await expect(kits.remove('k1')).rejects.toThrow('disk full')
    expect(kits.kitsFor(103, 'MIDDLE')).toHaveLength(1)
    expect(kits.error.value).toContain('disk full')
  })
})

describe('useItemClasses', () => {
  it('reads the classes once per patch, and retries a failed read on the next patch', async () => {
    const patch = ref('16.21.1')
    const apiGet = vi.fn()
      .mockResolvedValueOnce({ patch: '16.21', items: [{ itemId: 3031, classes: ['Marksman'] }, { itemId: 3190, classes: ['Support', 'Tank'] }] })
      .mockRejectedValueOnce(new Error('offline'))
      .mockResolvedValueOnce({ patch: '16.23', items: [{ itemId: 3089, classes: ['Mage'] }] })
    vi.stubGlobal('watch', watch)
    vi.stubGlobal('apiGet', apiGet)
    vi.stubGlobal('useChampionStatics', () => ({ patch }))
    const { useItemClasses } = await import('~/composables/useItemClasses')

    const { classes } = useItemClasses()
    await flush()
    expect(classes.value).toEqual({ 3031: ['Marksman'], 3190: ['Support', 'Tank'] })
    expect(apiGet).toHaveBeenCalledWith('/items/classes', { patch: '16.21.1' }, { background: true })
    useItemClasses()
    await flush()
    expect(apiGet).toHaveBeenCalledTimes(1)

    patch.value = '16.22.1'
    await flush()
    expect(classes.value).toEqual({ 3031: ['Marksman'], 3190: ['Support', 'Tank'] })
    patch.value = '16.23.1'
    await flush()
    expect(classes.value).toEqual({ 3089: ['Mage'] })
  })
})

describe('useKitImport', () => {
  async function imports() {
    const { useKitImport } = await import('~/composables/useKitImport')
    return useKitImport()
  }

  it('sends the kit\'s blocks and spells on a click, and says so', async () => {
    tauri = true
    shell.invoke.mockResolvedValue(undefined)
    const kitImport = await imports()
    expect(kitImport.inChampSelect.value).toBe(true)

    await kitImport.importItems(kit(), 'Ahri')
    expect(shell.invoke).toHaveBeenCalledWith('import_kit_item_set', {
      championId: 103,
      champion: 'Ahri',
      blocks: [
        { title: 'Starter', items: [1056, 2003, 2003] },
        { title: 'Boots', items: [3020] },
        { title: 'Core · Item 3089', items: [6655, 3100, 3089] },
        { title: 'Path 2 · Item 4645', items: [6655, 4645] },
      ],
    })
    await kitImport.importSpells(kit())
    expect(shell.invoke).toHaveBeenCalledWith('champ_select_spells', { spell1Id: 4, spell2Id: 14 })
    expect(toasts.map(toast => toast.title)).toEqual(['Items imported', 'Spells set'])
  })

  it('tells the refusals apart', async () => {
    tauri = true
    shell.invoke.mockRejectedValueOnce('not-your-turn').mockRejectedValueOnce('client down').mockRejectedValueOnce('read-failed')
    const kitImport = await imports()
    await kitImport.importSpells(kit())
    await kitImport.importSpells(kit())
    await kitImport.importItems(kit(), 'Ahri')
    await kitImport.importItems(kit({ starter: [], boots: [], tree: [] }), 'Ahri')
    expect(toasts.map(toast => toast.title)).toEqual(['Not in champion select', 'Could not set the spells', 'Could not read your item sets', 'Nothing to import'])
  })

  it('sends nothing from the dev server', async () => {
    const kitImport = await imports()
    await kitImport.importItems(kit(), 'Ahri')
    await kitImport.importSpells(kit())
    expect(shell.invoke).not.toHaveBeenCalled()
    expect(toasts.every(toast => toast.description?.startsWith('Dev server'))).toBe(true)
  })
})

describe('useKitStep', () => {
  function game(items: number[], position = 'MIDDLE'): Ref<GameState> {
    return ref({ players: [{ isMe: true, champion: 'Ahri', position, items: items.map(itemId => ({ itemId })) }] } as unknown as GameState)
  }

  it('follows the active kit from the items held, and its starter until one is bought', async () => {
    const kits = await kitsStore()
    await kits.save(kit())
    await kits.setActive(103, 'MIDDLE', 'k1')
    const { useKitStep } = await import('~/composables/useKitStep')

    const start = useKitStep(game([]), ref([]))
    expect(start.step.value).toEqual({ itemId: 6655, isBoots: false })
    expect(start.starter.value?.itemIds).toEqual([1056, 2003, 2003])

    const held = [1056, 6655, 4645]
    const late = useKitStep(game(held), ref(held))
    expect(late.starter.value).toBeNull()
    expect(late.step.value).toEqual({ itemId: 3020, isBoots: true })
    expect(late.row.value.map(step => step.state)).toEqual(['Completed', 'Next', 'Completed'])

    const elsewhere = useKitStep(game([], 'TOP'), ref([]))
    expect(elsewhere.kit.value).toBeNull()
    expect(elsewhere.step.value).toBeNull()
    expect(elsewhere.row.value).toEqual([])
  })
})

describe('useNextItemPanel with a kit', () => {
  const mains = {
    championId: 103,
    position: 'MIDDLE',
    patch: '26.20',
    build: {
      parentItemId: 6655,
      onTree: true,
      branchGames: 100,
      patchWindow: 1,
      candidates: [
        { itemId: 3089, share: 0.6, baseShare: 0.5, games: 60, wins: 33, reasons: [] },
        { itemId: 3135, share: 0.2, baseShare: 0.2, games: 20, wins: 10, reasons: [] },
      ],
    },
    boots: null,
    order: [{ itemId: 3089, isBoots: false, state: 'Next' as const }],
    situation: {},
  }

  async function panel(step: { itemId: number, isBoots: boolean } | null, withKit = true) {
    vi.stubGlobal('useNextItem', () => ({ answer: ref(mains), pending: ref(false), failed: ref(false), order: ref([]) }))
    vi.stubGlobal('useStarter', () => ({ starter: ref({ itemIds: [1055], games: 9, pickRate: 0.9, winRate: 0.5 }) }))
    vi.stubGlobal('useKitStep', () => ({
      kit: ref(withKit ? kit() : null),
      step: ref(step),
      row: ref([{ itemId: 6655, isBoots: false, state: 'Next' }]),
      starter: ref(null),
    }))
    const { useNextItemPanel } = await import('~/composables/useNextItemPanel')
    return useNextItemPanel(ref({ gold: 0, players: [{ isMe: true, items: [] }] } as unknown as GameState))
  }

  it('answers with the kit\'s step, without the mains\' figures', async () => {
    const view = await panel({ itemId: 6655, isBoots: false })
    expect(view.fromKit.value).toBe(true)
    expect(view.top.value).toMatchObject({ itemId: 6655, share: 0, reasons: [] })
    expect(view.why.value).toBeNull()
    expect(view.runnersUp.value).toEqual([])
    expect(view.order.value.map(step => step.itemId)).toEqual([6655])
    expect(view.starter.value).toBeNull()
    expect(view.build.value).toBeNull()
    expect(view.state.value).toBe('ready')
  })

  it('falls back to the mains\' answer once the kit is done, and says so', async () => {
    const view = await panel(null)
    expect(view.kitDone.value).toBe(true)
    expect(view.top.value?.itemId).toBe(3089)
    expect(view.runnersUp.value.map(candidate => candidate.itemId)).toEqual([3135])
  })

  it('is the mains\' answer without a kit', async () => {
    const view = await panel(null, false)
    expect(view.kitDone.value).toBe(false)
    expect(view.fromKit.value).toBe(false)
    expect(view.starter.value?.itemIds).toEqual([1055])
  })
})
