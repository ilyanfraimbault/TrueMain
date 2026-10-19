import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'

// `surface` is every card and every match row of the site and the app. A
// `backdrop-filter` on it made the compositor blur the backdrop of each of them
// on every scrolled frame, over a flat ground where the blur shows nothing
// (#2129). The blur is an opt-in, `surface-blur`, for the panes something
// moves behind.
const theme = readFileSync(join(process.cwd(), 'layers/common/app/assets/css/theme.css'), 'utf8')

function utility(name: string): string {
  const body = theme.match(new RegExp(`@utility ${name} \\{([^}]*)\\}`))
  if (!body) throw new Error(`no @utility ${name} in theme.css`)
  return body[1]!
}

describe('surface', () => {
  it('paints its fill and hairline without blurring its backdrop', () => {
    expect(utility('surface')).toContain('background-color: var(--ui-bg-surface)')
    expect(utility('surface')).not.toContain('backdrop-filter')
  })

  it('keeps the blur behind an explicit opt-in', () => {
    expect(utility('surface-blur')).toContain('backdrop-filter: blur(12px)')
  })
})
