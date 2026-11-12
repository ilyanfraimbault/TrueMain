import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import routes from '../../src-tauri/api-routes.json'

/**
 * The shell forwards only the reads its route table lists
 * (`src-tauri/api-routes.json`), so a read the app makes and the table lacks
 * fails in the app alone — silently, for the shared pages that catch it.
 * Every API read the app's code and the shared layer it renders
 * (`web/layers/common`) name has to be a route.
 */

// Vitest runs from `desktop/app`.
const repo = resolve(process.cwd(), '../..')
const ROOTS = ['desktop/app/app', 'web/layers/common/app']
/** A read's path given as a variable, which this check cannot follow, and why that is safe. */
const VARIABLE_PATHS: Record<string, string> = {
  // The `/static/*` lookups, answered by the app itself (`utils/static-endpoints.ts`).
  'web/layers/common/app/composables/useBuildAssets.ts': 'static',
  // The fetchers themselves.
  'desktop/app/app/composables/useApi.ts': 'fetcher',
}

function sources(): string[] {
  const files: string[] = []
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name)
      if (statSync(path).isDirectory()) {
        if (name !== 'node_modules' && name !== 'generated') walk(path)
      }
      else if (/\.(ts|vue)$/.test(name)) {
        files.push(path)
      }
    }
  }
  ROOTS.forEach(root => walk(join(repo, root)))
  return files
}

interface Read { method: 'GET' | 'POST', path: string, file: string }

/** The reads a file makes: `apiGet`/`apiPost` and the shared pages' `apiFetch`, by their first argument. */
function readsOf(file: string, text: string): { reads: Read[], variable: boolean } {
  const reads: Read[] = []
  let variable = false
  const call = /\b(apiFetch|apiGet|apiPost)\s*(?:<[^()]*?>)?\(\s*([`'"])((?:(?!\2).)*)\2([^)]*)/gs
  for (const [, fn, , literal, rest] of text.matchAll(call)) {
    const path = literal!.replace(/\$\{[^}]*\}/g, '{}').replace(/\?.*$/, '').replace(/^\/api(?=\/)/, '')
    const post = fn === 'apiPost' || /method:\s*'POST'/.test(rest!)
    reads.push({ method: post ? 'POST' : 'GET', path, file })
  }
  if (/\b(apiFetch|apiGet|apiPost)\s*(?:<[^()]*?>)?\(\s*[A-Za-z_]/.test(text)) variable = true
  return { reads, variable }
}

/** Whether `path` (parameters as `{}`) is the table's `pattern` (parameters named). */
function fits(pattern: string, path: string): boolean {
  const expected = pattern.split('/')
  const asked = path.split('/')
  return expected.length === asked.length
    && expected.every((segment, index) => (segment.startsWith('{') ? /\{\}/.test(asked[index]!) : segment === asked[index]))
}

describe('the shell\'s route table', () => {
  const found = sources().map((file) => {
    const { reads, variable } = readsOf(file, readFileSync(file, 'utf8'))
    return { file: relative(repo, file), reads, variable }
  })
  const reads = found.flatMap(entry => entry.reads.map(read => ({ ...read, file: entry.file })))
    .filter(read => !read.path.startsWith('/static/'))

  it('lists every read the app makes', () => {
    const missing = reads.filter(read => !routes.some(route => route.method === read.method && fits(route.path, read.path)))
    expect(missing).toEqual([])
    expect(reads.length).toBeGreaterThan(20)
  })

  it('lists no route nothing reads', () => {
    const unread = routes.filter(route => !reads.some(read => read.method === route.method && fits(route.path, read.path)))
    expect(unread).toEqual([])
  })

  it('can follow every read, or knows why not', () => {
    const unfollowed = found.filter(entry => entry.variable && !(entry.file in VARIABLE_PATHS)).map(entry => entry.file)
    expect(unfollowed).toEqual([])
  })
})
