import { flushPromises } from '@vue/test-utils'
import { mountSuspended } from '@nuxt/test-utils/runtime'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AppSearch, UApp } from '#components'

/**
 * ⌘K works wherever the focus is. A shortcut is off inside an input by default,
 * and WebKit fires no blur when a focused input is removed, so in the macOS app
 * the closed palette's input kept the shortcut off for good.
 */
function pressShortcut() {
  const mac = /Macintosh;/.test(navigator.userAgent)
  window.dispatchEvent(new KeyboardEvent('keydown', { key: 'k', metaKey: mac, ctrlKey: !mac, cancelable: true }))
}

const paletteInput = () => document.querySelector('[role="dialog"] input')

let unmount: (() => void) | undefined

async function mountSearch() {
  const wrapper = await mountSuspended(defineComponent({
    setup: () => () => h(UApp, null, () => h(AppSearch, { variant: 'field', shortcut: true })),
  }), { attachTo: document.body })
  unmount = () => wrapper.unmount()
  await flushPromises()
}

afterEach(() => {
  unmount?.()
  document.body.innerHTML = ''
})

describe('search shortcut', () => {
  it('opens the palette while a field of the page has the focus', async () => {
    await mountSearch()
    const field = document.body.appendChild(document.createElement('input'))
    field.focus()
    expect(document.activeElement).toBe(field)

    pressShortcut()
    await vi.waitFor(() => expect(paletteInput()).not.toBeNull())
  })

  it('closes the palette from its own input', async () => {
    await mountSearch()
    pressShortcut()
    const input = await vi.waitFor(() => {
      const element = paletteInput() as HTMLInputElement | null
      expect(element).not.toBeNull()
      return element!
    })
    input.focus()

    pressShortcut()
    await vi.waitFor(() => expect(document.querySelector('[role="dialog"][data-state="open"]')).toBeNull())
  })

  it('stays out of the way of another open dialog', async () => {
    await mountSearch()
    const other = document.body.appendChild(document.createElement('div'))
    other.setAttribute('role', 'dialog')
    other.dataset.state = 'open'

    pressShortcut()
    await flushPromises()
    await new Promise(resolve => setTimeout(resolve, 100))
    expect(paletteInput()).toBeNull()
  })
})
