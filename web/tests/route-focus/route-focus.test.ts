import { afterEach, describe, expect, it } from 'vitest'
import { MAIN_CONTENT_ID, focusMainContent, shouldMoveFocus } from '~/utils/route-focus'

/**
 * Focus handling after client-side navigation (#1616). The rule worth pinning
 * is which navigations move focus: a filter click on the champion page is a
 * same-path navigation, and moving focus there would yank the keyboard out of
 * the control being used.
 */
const route = (path: string, matched: unknown[] = [{}]) => ({ path, matched })

describe('shouldMoveFocus', () => {
  it('moves focus when the path changes', () => {
    expect(shouldMoveFocus(route('/champions/ahri'), route('/champions'))).toBe(true)
  })

  it('leaves focus alone on a query-only navigation', () => {
    expect(shouldMoveFocus(route('/champions/ahri'), route('/champions/ahri'))).toBe(false)
  })

  it('leaves focus alone on the initial navigation', () => {
    expect(shouldMoveFocus(route('/champions'), route('/', []))).toBe(false)
  })
})

describe('focusMainContent', () => {
  afterEach(() => {
    document.body.innerHTML = ''
  })

  it('focuses the main region', () => {
    document.body.innerHTML = `<button>nav</button><main id="${MAIN_CONTENT_ID}" tabindex="-1"></main>`
    document.querySelector('button')!.focus()

    expect(focusMainContent()).toBe(true)
    expect(document.activeElement?.id).toBe(MAIN_CONTENT_ID)
  })

  it('reports failure when the region is missing', () => {
    expect(focusMainContent()).toBe(false)
  })
})
