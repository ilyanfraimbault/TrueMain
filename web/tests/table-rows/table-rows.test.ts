import { describe, expect, it, vi } from 'vitest'
import { clickSelectableRow, wantsNewTab } from '~/utils/table-rows'

function selectableRow() {
  const table = document.createElement('table')
  const row = document.createElement('tr')
  row.setAttribute('data-slot', 'tr')
  row.setAttribute('role', 'button')
  row.tabIndex = 0
  const cell = document.createElement('td')
  const button = document.createElement('button')
  cell.append(button)
  row.append(cell)
  table.append(row)
  document.body.append(table)
  return { row, button }
}

function press(target: HTMLElement, key: string) {
  const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true })
  target.dispatchEvent(event)
  return event
}

describe('clickSelectableRow', () => {
  it.each(['Enter', ' '])('turns %j on a focused row into its click', (key) => {
    const { row } = selectableRow()
    const click = vi.fn()
    row.addEventListener('click', click)
    row.addEventListener('keydown', clickSelectableRow)

    const event = press(row, key)

    expect(click).toHaveBeenCalledTimes(1)
    // Space would otherwise scroll the page as well.
    expect(event.defaultPrevented).toBe(true)
  })

  it('leaves keys pressed on a control inside the row to that control', () => {
    const { row, button } = selectableRow()
    const click = vi.fn()
    row.addEventListener('click', click)
    row.addEventListener('keydown', clickSelectableRow)

    const event = press(button, 'Enter')

    expect(click).not.toHaveBeenCalled()
    expect(event.defaultPrevented).toBe(false)
  })

  it('ignores every other key', () => {
    const { row } = selectableRow()
    const click = vi.fn()
    row.addEventListener('click', click)
    row.addEventListener('keydown', clickSelectableRow)

    press(row, 'a')

    expect(click).not.toHaveBeenCalled()
  })
})

describe('wantsNewTab', () => {
  it('reads Ctrl, Cmd and the middle button as a new tab', () => {
    expect(wantsNewTab(new MouseEvent('click', { ctrlKey: true }))).toBe(true)
    expect(wantsNewTab(new MouseEvent('click', { metaKey: true }))).toBe(true)
    expect(wantsNewTab(new MouseEvent('auxclick', { button: 1 }))).toBe(true)
    expect(wantsNewTab(new MouseEvent('click'))).toBe(false)
    expect(wantsNewTab(new KeyboardEvent('keydown', { key: 'Enter' }))).toBe(false)
  })
})
