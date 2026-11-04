import { describe, expect, it } from 'vitest'
import { newTabLinkOf } from '../app/utils/external-link'

function linkWith(attributes: Record<string, string>): HTMLElement {
  const link = document.createElement('a')
  for (const [name, value] of Object.entries(attributes)) link.setAttribute(name, value)
  const icon = document.createElement('span')
  link.append(icon)
  return icon
}

describe('newTabLinkOf', () => {
  it('reads the address of the new-tab link a click landed in', () => {
    const icon = linkWith({ href: 'https://www.twitch.tv/faker', target: '_blank' })
    expect(newTabLinkOf(icon)).toBe('https://www.twitch.tv/faker')
  })

  it('leaves the app\'s own links and anything else alone', () => {
    expect(newTabLinkOf(linkWith({ href: 'https://www.twitch.tv/faker' }))).toBeNull()
    expect(newTabLinkOf(linkWith({ href: '/players/faker', target: '_blank' }))).toBeNull()
    expect(newTabLinkOf(linkWith({ href: 'javascript:alert(1)', target: '_blank' }))).toBeNull()
    expect(newTabLinkOf(document.createElement('div'))).toBeNull()
    expect(newTabLinkOf(null)).toBeNull()
  })
})
