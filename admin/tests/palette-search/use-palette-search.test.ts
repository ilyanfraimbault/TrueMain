import { flushPromises } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { PersonaListRow } from '~~/shared/types/ops'
import { usePaletteSearch } from '~/composables/usePaletteSearch'
import { buildSearchGroups } from '~/utils/navigation'
import { opsCache } from '~/utils/ops-cache'

// The ⌘K palette's groups (#2290): pages always, instantly; the known-players
// request only for a settled term of two characters or more.

const row = (id: string, displayName: string) => ({ id, slug: id, displayName, removedAtUtc: null }) as PersonaListRow
const PAGE_COUNT = buildSearchGroups().length

let searchPersonas: ReturnType<typeof vi.fn>
let fetchChampions: ReturnType<typeof vi.fn>

beforeEach(() => {
  opsCache.clear()
  searchPersonas = vi.fn(async (search: string) => ({ personas: [row(`id-${search}`, search)], total: 1, page: 1, pageSize: 6 }))
  fetchChampions = vi.fn(async () => [{ championId: 103, name: 'Ahri', iconUrl: 'https://cdn/ahri.png' }])
  // The debounce is VueUse's; here every change settles at once.
  vi.stubGlobal('refDebounced', (source: unknown) => source)
  vi.stubGlobal('searchPersonas', searchPersonas)
  vi.stubGlobal('$fetch', fetchChampions)
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function ids(groups: { id: string }[]) {
  return groups.map(group => group.id)
}

describe('usePaletteSearch', () => {
  it('lists the pages alone and sends nothing under two characters', async () => {
    const { term, groups } = usePaletteSearch()
    expect(groups.value).toHaveLength(PAGE_COUNT)
    term.value = 'a'
    await flushPromises()
    expect(searchPersonas).not.toHaveBeenCalled()
    expect(fetchChampions).not.toHaveBeenCalled()
    expect(groups.value).toHaveLength(PAGE_COUNT)
  })

  it('puts the known players and champions it found above the pages', async () => {
    const { term, groups, loading } = usePaletteSearch()
    term.value = 'ahri'
    await flushPromises()
    expect(searchPersonas).toHaveBeenCalledWith('ahri', 6)
    expect(loading.value).toBe(false)
    expect(ids(groups.value).slice(0, 2)).toEqual(['entity-known-players', 'entity-champions'])
    expect(groups.value).toHaveLength(PAGE_COUNT + 2)

    // A shorter or longer spelling keeps the last answer up until the next lands.
    term.value = 'ahr'
    expect(ids(groups.value)[0]).toBe('entity-known-players')
    term.value = 'zz'
    expect(ids(groups.value)).not.toContain('entity-known-players')
    await flushPromises()
    expect(groups.value[0]!.items![0]!.label).toBe('zz')

    // The champion list is loaded once, and stored for `useChampionStatic`.
    expect(fetchChampions).toHaveBeenCalledTimes(1)
    expect(opsCache.get('static:champions')?.value).toHaveLength(1)
  })

  it('keeps only the answer for the latest term', async () => {
    const answers: Record<string, (value: unknown) => void> = {}
    searchPersonas.mockImplementation((search: string) => new Promise((resolve) => {
      answers[search] = resolve
    }))
    const { term, groups, loading } = usePaletteSearch()
    term.value = 'fa'
    await flushPromises()
    term.value = 'faker'
    await flushPromises()
    expect(loading.value).toBe(true)
    answers.faker!({ personas: [row('p1', 'Faker')] })
    await flushPromises()
    answers.fa!({ personas: [row('p2', 'Fate')] })
    await flushPromises()
    expect(loading.value).toBe(false)
    expect(groups.value[0]!.items!.map(item => item.label)).toEqual(['Faker'])
  })

  it('drops the known players on a failed call, and clears them when the term shrinks', async () => {
    const { term, groups } = usePaletteSearch()
    term.value = 'faker'
    await flushPromises()
    expect(ids(groups.value)).toContain('entity-known-players')

    searchPersonas.mockRejectedValueOnce(new Error('down'))
    term.value = 'fakers'
    await flushPromises()
    expect(ids(groups.value)).not.toContain('entity-known-players')

    term.value = 'f'
    await flushPromises()
    expect(groups.value).toHaveLength(PAGE_COUNT)
  })

  it('reads the champion list from the shared cache, and retries a failed load', async () => {
    fetchChampions.mockRejectedValueOnce(new Error('down'))
    const first = usePaletteSearch()
    first.term.value = 'ahri'
    await flushPromises()
    expect(ids(first.groups.value)).not.toContain('entity-champions')
    first.term.value = 'ahri '
    first.term.value = 'ahr'
    await flushPromises()
    expect(fetchChampions).toHaveBeenCalledTimes(2)
    expect(ids(first.groups.value)).toContain('entity-champions')

    const second = usePaletteSearch()
    second.term.value = 'ahri'
    await flushPromises()
    expect(fetchChampions).toHaveBeenCalledTimes(2)
    expect(ids(second.groups.value)).toContain('entity-champions')
  })
})
