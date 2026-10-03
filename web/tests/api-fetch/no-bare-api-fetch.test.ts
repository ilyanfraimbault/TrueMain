// @vitest-environment node
import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * Backend calls go through `useApi` / `useApiFetch` (#1619), which forward the
 * visitor during SSR (#1557) and normalise errors. A bare `$fetch('/api/…')` is the
 * exact shape of the #1557 bug, so a new one fails here instead of in review.
 *
 * `LEGACY` lists the files that still carry one. It may only shrink: migrate a file,
 * delete its line. A file that has to stay (a hand-rolled fetcher, a server-only OG
 * component) says why.
 */
const LEGACY = new Set([
  // Server-rendered OG cards: Nitro handlers that already forward the visitor.
  'components/OgImage/Champion.satori.vue',
  'components/OgImage/Truemain.satori.vue',
  // Hand-rolled per-viewer fetchers, client-only by construction — never through the
  // shared SSR payload (decisions/web-frontend-rules.md).
  'composables/useTruemainActivity.ts',
  'composables/useTruemainRankHistory.ts',
  // Reads a Nitro static route, not the backend, and is being reworked for the
  // prerendered pages (#1617) — migrate it once that lands.
  'plugins/champion-slugs.ts',
])

const APP_DIR = fileURLToPath(new URL('../../app', import.meta.url))
// The layer shared with the desktop app (#1732): a bare `/api` fetch there is
// also a call the app cannot make, since it has no `/api` to reach.
const LAYER_DIR = fileURLToPath(new URL('../../layers/common/app', import.meta.url))
const BARE_API_FETCH = /\$fetch\s*(?:<[^()]*>)?\(\s*[`'"]\/api\//

function sourceFiles(dir: string): string[] {
  return readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const path = join(dir, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    return /\.(?:ts|vue)$/.test(entry.name) ? [path] : []
  })
}

function withoutComments(source: string): string {
  return source
    .replace(/\/\*[\s\S]*?\*\//g, '')
    .replace(/<!--[\s\S]*?-->/g, '')
    .replace(/^\s*\/\/.*$/gm, '')
}

const offenders = [APP_DIR, LAYER_DIR]
  .flatMap(dir => sourceFiles(dir)
    .filter(path => BARE_API_FETCH.test(withoutComments(readFileSync(path, 'utf8'))))
    .map(path => `${dir === LAYER_DIR ? 'layers/common/' : ''}${relative(dir, path).split('\\').join('/')}`))
  .sort()

describe('backend calls', () => {
  it('go through useApi / useApiFetch outside the recorded exceptions', () => {
    expect(offenders.filter(path => !LEGACY.has(path))).toEqual([])
  })

  it('keep the exception list honest: every entry still needs its exception', () => {
    expect([...LEGACY].filter(path => !offenders.includes(path))).toEqual([])
  })
})
