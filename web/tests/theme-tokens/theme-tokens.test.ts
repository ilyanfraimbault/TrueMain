import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
// @ts-expect-error — a plain .mjs build script, no declaration file
import { renderThemeTokens, syncThemeTokens } from '../../scripts/theme-tokens.mjs'

const read = (path: string) => readFileSync(join(process.cwd(), path), 'utf8')

describe('theme tokens', () => {
  it('match theme.css: run `npm run tokens` after changing a colour there', () => {
    expect(read('shared/utils/theme-tokens.ts')).toBe(renderThemeTokens(read('layers/common/app/assets/css/theme.css')))
  })

  it('refuse one token with two values', () => {
    expect(() => renderThemeTokens('--color-a: #000000; --color-a: #ffffff;')).toThrow()
  })
  it('write the file, then find it current; and find a hand edit stale', () => {
    const disk = new Map([['theme.css', '--color-ink-50: #F7F7F8;']])
    const files = { read: (path: string) => disk.get(path) ?? '', write: (path: string, text: string) => void disk.set(path, text) }
    const paths = { files, themeCss: 'theme.css', tokensTs: 'tokens.ts' }

    syncThemeTokens({ ...paths, check: false })
    expect(disk.get('tokens.ts')).toContain("'ink-50': '#f7f7f8'")
    expect(syncThemeTokens({ ...paths, check: true })).toBe(true)

    disk.set('tokens.ts', 'edited by hand')
    expect(syncThemeTokens({ ...paths, check: true })).toBe(false)
  })
})
