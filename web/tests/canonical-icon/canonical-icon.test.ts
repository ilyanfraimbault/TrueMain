import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { describe, expect, it } from 'vitest'
import { ICON_FETCH_SIZE } from '~~/app/utils/icon-fetch'

/**
 * Guards the invariant #1000 was opened to restore, rather than the four lines
 * of `useCanonicalIcon()` itself.
 *
 * The helper is trivial; what is not trivial is that *every* icon call site
 * goes through it. That was true before too — informally — and it drifted: the
 * same lane glyph ended up fetched at 12, 20, 22 and 64 px by four components,
 * and the search palette bound the raw Data Dragon URL, shipping a 30 KB PNG
 * into a 20 px box. A unit test over a mocked `useImage()` would not have
 * caught any of that, because each of those call sites was individually
 * correct — they just were not the *same*.
 *
 * So this asserts the rule directly: nothing outside the allowed files builds
 * its own IPX URL. If you are here because this test failed, the fix is almost
 * always to call `useCanonicalIcon()` instead of `useImage()`.
 */

// Vitest's root is `web/` (vitest.config.ts sits there), and the happy-dom
// environment leaves `import.meta.url` as a non-file URL, so resolve from cwd.
const APP_DIR = join(process.cwd(), 'app')

// The only two files allowed to reach for `useImage()` directly.
const ALLOWED = new Set([
  // Owns the canonical URL shape; everything else delegates to it.
  'composables/useCanonicalIcon.ts',
  // Deliberate exception: `.svg` sources that IPX serves through as
  // image/svg+xml. Asking for a raster format would trade a vector that stays
  // crisp at any DPR for a 20 px bitmap. See decisions.md.
  'components/RankIcon.vue',
])

function sourceFiles(dir: string, prefix = ''): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    const rel = prefix ? `${prefix}/${entry}` : entry
    if (statSync(full).isDirectory()) return sourceFiles(full, rel)
    return /\.(?:vue|ts)$/.test(entry) ? [rel] : []
  })
}

describe('canonical icon URLs', () => {
  it('is the size SkeletonImage and the shared helper both fetch at', () => {
    expect(ICON_FETCH_SIZE).toBe(64)
  })

  it('leaves useImage() to the helper and the one documented SVG exception', () => {
    const offenders = sourceFiles(APP_DIR).filter((rel) => {
      if (ALLOWED.has(rel)) return false
      return /\buseImage\s*\(/.test(readFileSync(join(APP_DIR, rel), 'utf8'))
    })

    expect(offenders).toEqual([])
  })

  it('has no component binding a raw Data Dragon or Community Dragon URL to an <img>', () => {
    // The regression the search palette shipped: `:src="champion.iconUrl"`
    // where `iconUrl` is the upstream CDN URL, so the browser fetches a
    // full-size asset from Riot with no resize, no WebP and no cache of ours.
    const offenders = sourceFiles(APP_DIR).filter((rel) => {
      if (!rel.endsWith('.vue')) return false
      const source = readFileSync(join(APP_DIR, rel), 'utf8')
      return /:src="(?:https?:)?\/\/(?:ddragon\.leagueoflegends\.com|raw\.communitydragon\.org)/.test(source)
    })

    expect(offenders).toEqual([])
  })
})
