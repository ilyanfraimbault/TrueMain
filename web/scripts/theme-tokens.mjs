// Emits shared/utils/theme-tokens.ts, the hex value of every `--color-*` token in
// the design system's theme.css, for the places that cannot read a CSS variable:
// the share cards (Satori), chart libraries, the admin. `--check` fails when the
// committed file is stale; `tests/theme-tokens` runs the same comparison in CI.
import { readFileSync, writeFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'

export function renderThemeTokens(css) {
  const tokens = new Map()
  for (const [, name, hex] of css.matchAll(/--color-([a-z0-9-]+):\s*(#[0-9a-fA-F]{6})\b/g)) {
    const value = hex.toLowerCase()
    const known = tokens.get(name)
    if (known && known !== value) throw new Error(`--color-${name} is ${known} and ${value} in theme.css`)
    tokens.set(name, value)
  }
  const entries = [...tokens].map(([name, value]) => `  '${name}': '${value}',`).join('\n')
  return `// Generated from layers/common/app/assets/css/theme.css by scripts/theme-tokens.mjs — do not edit.
// Regenerate with \`npm run tokens\`.

/** Every \`--color-*\` token of the design system, as a hex value. */
export const THEME_HEX = {
${entries}
} as const

export type ThemeColor = keyof typeof THEME_HEX
`
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  const THEME_CSS = fileURLToPath(new URL('../layers/common/app/assets/css/theme.css', import.meta.url))
  const TOKENS_TS = fileURLToPath(new URL('../shared/utils/theme-tokens.ts', import.meta.url))
  const rendered = renderThemeTokens(readFileSync(THEME_CSS, 'utf8'))
  if (process.argv.includes('--check')) {
    if (readFileSync(TOKENS_TS, 'utf8') !== rendered) {
      console.error('shared/utils/theme-tokens.ts is stale: run `npm run tokens`.')
      process.exit(1)
    }
  }
  else {
    writeFileSync(TOKENS_TS, rendered)
  }
}
