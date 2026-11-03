import { afterEach, describe, expect, it, vi } from 'vitest'
import { reactive, ref } from 'vue'
import { useVerdictTitle } from '~/composables/useVerdictTitle'
import { GROUPS } from '~/utils/navigation'
import {
  APP_TITLE,
  DEFAULT_FAVICON,
  pageTitle,
  titleTemplate,
  verdictFavicon,
  verdictTitle,
} from '~/utils/page-title'

// Browser-tab titles (#2296): every destination of the sidebar/⌘K table is
// named, tabs by their query, and the health verdict marks the tab only when it
// is not green.

describe('titleTemplate', () => {
  it('suffixes the app name, or stands alone without a title', () => {
    expect(titleTemplate('Health')).toBe(`Health · ${APP_TITLE}`)
    expect(titleTemplate(undefined)).toBe(APP_TITLE)
    expect(titleTemplate('')).toBe(APP_TITLE)
  })
})

describe('pageTitle', () => {
  it('names a page by its sidebar label', () => {
    expect(pageTitle('/')).toBe('Overview')
    expect(pageTitle('/health')).toBe('Health')
    expect(pageTitle('/known-players')).toBe('Known players')
  })

  it('names a tab from its palette entry when the query matches', () => {
    expect(pageTitle('/processes', { view: 'riot-api' })).toBe('Processes — Riot API')
    expect(pageTitle('/known-players', { status: 'homonyms' })).toBe('Known players — Homonyms')
    expect(pageTitle('/logs', { view: 'crashes', level: 'Error' })).toBe('Logs — Crashes')
  })

  it('falls back to the page for an unlisted query value', () => {
    expect(pageTitle('/processes', { view: 'runs' })).toBe('Processes')
    expect(pageTitle('/processes', { view: ['riot-api'] })).toBe('Processes')
  })

  it('leaves a route the table does not name untitled', () => {
    expect(pageTitle('/login')).toBeUndefined()
  })

  it('titles every destination of the table, so a new entry is named for free', () => {
    for (const [label, , to] of GROUPS.flatMap(group => [...group.pages, ...group.tabs])) {
      const [path = '/', search = ''] = to.split('?')
      const query = Object.fromEntries(new URLSearchParams(search))
      expect(pageTitle(path, query)).toBe(label.replace(' → ', ' — '))
    }
  })
})

describe('verdictTitle / verdictFavicon', () => {
  it('prefixes and dots the alarming verdicts', () => {
    expect(verdictTitle('Health', 'red')).toBe('● Red — Health')
    expect(verdictTitle('Overview', 'amber')).toBe('● Amber — Overview')
    expect(verdictFavicon('red')).toEqual({ href: '/favicon-red.svg', type: 'image/svg+xml' })
    expect(verdictFavicon('amber')).toEqual({ href: '/favicon-amber.svg', type: 'image/svg+xml' })
  })

  it('leaves green, unknown and a missing payload alone', () => {
    for (const status of ['green', 'unknown', null, undefined, 'purple']) {
      expect(verdictTitle('Health', status)).toBe('Health')
      expect(verdictFavicon(status)).toEqual({ href: DEFAULT_FAVICON })
    }
  })
})

describe('useVerdictTitle', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('follows the verdict the page already holds', () => {
    let head: { title: { value: string }, link: { value: { href: string }[] } } | undefined
    vi.stubGlobal('useRoute', () => reactive({ path: '/health', query: {} }))
    vi.stubGlobal('useHead', (input: typeof head) => {
      head = input
    })
    const status = ref<string | undefined>()
    useVerdictTitle(status)
    expect(head?.title.value).toBe('Health')
    expect(head?.link.value[0]?.href).toBe(DEFAULT_FAVICON)

    status.value = 'red'
    expect(head?.title.value).toBe('● Red — Health')
    expect(head?.link.value[0]?.href).toBe('/favicon-red.svg')

    status.value = 'green'
    expect(head?.title.value).toBe('Health')
    expect(head?.link.value[0]?.href).toBe(DEFAULT_FAVICON)
  })
})
