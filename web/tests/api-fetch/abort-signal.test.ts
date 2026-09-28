// @vitest-environment node
import { readdirSync, readFileSync } from 'node:fs'
import { join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * A backend request that nobody is waiting for any more must be cancelled, not
 * left to run (#1712). Nuxt aborts the `signal` it hands a `useAsyncData`
 * handler on unmount, on a key change and on a superseding refresh, but the
 * HTTP request only stops if the handler forwards that signal to its fetcher;
 * otherwise every page left mid-load keeps its requests — and the backend reads
 * behind them — running to completion. The hand-rolled fetchers own an
 * `AbortController` for the same reason.
 *
 * Source-level on purpose, like `no-bare-api-fetch.test.ts`: a new composable
 * that forgets the signal fails here instead of on a fast-clicking visitor.
 */
const APP_DIR = fileURLToPath(new URL('../../app', import.meta.url))

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

const BACKEND_CALL = /\bapiFetch\s*(?:<[^()]*>)?\(|\$fetch\s*(?:<[^()]*>)?\(\s*[`'"]\/api\//
const FORWARDS_SIGNAL = /\bsignal\b/

// Calls with no request lifecycle to cut short.
const EXEMPT = new Set([
  // Server-rendered OG cards: rendered once per crawler hit, nobody navigates away.
  'components/OgImage/Champion.satori.vue',
  'components/OgImage/Truemain.satori.vue',
  // Boot-time lookup the whole visit depends on; there is no "leaving" it.
  'plugins/champion-slugs.ts',
  'plugins/static-prefetch.client.ts',
])

const offenders = sourceFiles(APP_DIR)
  .map(path => ({ path: relative(APP_DIR, path).split('\\').join('/'), source: withoutComments(readFileSync(path, 'utf8')) }))
  // Dev-only fixture pages, never reachable in production.
  .filter(({ path }) => !path.startsWith('pages/dev/') && !EXEMPT.has(path))
  .filter(({ source }) => BACKEND_CALL.test(source) && !FORWARDS_SIGNAL.test(source))
  .map(({ path }) => path)
  .sort()

describe('backend calls', () => {
  it('forward an abort signal, so a request nobody waits for is cancelled', () => {
    expect(offenders).toEqual([])
  })
})
