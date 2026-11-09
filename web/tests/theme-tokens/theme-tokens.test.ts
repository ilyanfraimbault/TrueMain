import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
// @ts-expect-error — a plain .mjs build script, no declaration file
import { renderThemeTokens } from '../../scripts/theme-tokens.mjs'

const read = (path: string) => readFileSync(join(process.cwd(), path), 'utf8')

describe('theme tokens', () => {
  it('match theme.css: run `npm run tokens` after changing a colour there', () => {
    expect(read('shared/utils/theme-tokens.ts')).toBe(renderThemeTokens(read('layers/common/app/assets/css/theme.css')))
  })

  it('refuse one token with two values', () => {
    expect(() => renderThemeTokens('--color-a: #000000; --color-a: #ffffff;')).toThrow()
  })
})
