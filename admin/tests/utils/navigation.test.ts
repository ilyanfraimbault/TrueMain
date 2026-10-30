import { describe, expect, it } from 'vitest'
import { buildSearchGroups, NAV_GROUPS, TAB_ENTRIES } from '~/utils/navigation'

// The ⌘K palette (#1415) is the only way to reach a tab by name: the sidebar
// carries the hubs alone. Its groups must mirror the sidebar's, and every tab
// entry must deep-link with the exact query value its page parses.

const groups = buildSearchGroups()

function find(label: string) {
  return groups.flatMap(group => group.items ?? []).find(item => item.label === label)
}

describe('buildSearchGroups', () => {
  it('mirrors the sidebar groups in order', () => {
    expect(groups.map(group => group.label)).toEqual(['Monitor', 'Data', 'Accounts', 'System'])
    expect(groups.map(group => group.id)).toEqual(['monitor', 'data', 'accounts', 'system'])
  })

  it('lists every sidebar page, without the group labels', () => {
    const pages = NAV_GROUPS.flat().filter(item => item.type !== 'label')
    for (const page of pages) {
      expect(find(String(page.label))?.to).toBe(page.to)
    }
    expect(groups.flatMap(group => group.items ?? []).some(item => item.type === 'label')).toBe(false)
  })

  it('appends the tab entries under their hub group', () => {
    const accounts = groups.find(group => group.id === 'accounts')
    expect(accounts?.items?.slice(-TAB_ENTRIES.Accounts!.length)).toEqual(TAB_ENTRIES.Accounts)
    expect(groups.find(group => group.id === 'data')?.items?.every(item => !String(item.label).includes('→'))).toBe(true)
  })

  it('deep-links the known players and scrapers tabs (#2299)', () => {
    expect(find('Known players → Open conflicts')?.to).toBe('/known-players?status=open')
    expect(find('Known players → Resolved conflicts')?.to).toBe('/known-players?status=resolved')
    expect(find('Known players → Homonyms')?.to).toBe('/known-players?status=homonyms')
    expect(find('Processes → Scrapers')?.to).toBe('/processes?view=scrapers')
    for (const label of ['Known players → Homonyms', 'Processes → Scrapers']) {
      expect(find(label)?.icon).toMatch(/^i-lucide-/)
    }
  })

  it('lists the Audit page under System (#2288)', () => {
    const system = groups.find(group => group.id === 'system')
    expect(system?.items?.find(item => item.label === 'Audit')).toMatchObject({ to: '/audit', icon: 'i-lucide-history' })
  })

  it('tolerates a group without a label or tab entries', () => {
    expect(buildSearchGroups([[{ label: 'Solo', to: '/solo' }]], {})).toEqual([
      { id: '', label: '', items: [{ label: 'Solo', icon: undefined, to: '/solo' }] },
    ])
  })
})
