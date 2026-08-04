import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useCanonicalIcon } from '~~/app/composables/useCanonicalIcon'
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

describe('useCanonicalIcon', () => {
  // `useImage()` is a Nuxt auto-import, so the composable resolves it off the
  // global scope at call time — which is exactly what lets it be stubbed here.
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function stubIpx() {
    const ipx = vi.fn((src: string, modifiers: unknown) => `/_ipx/${JSON.stringify(modifiers)}/${src}`)
    vi.stubGlobal('useImage', () => ipx)
    return ipx
  }

  it('asks for a square ICON_FETCH_SIZE fetch in WebP', () => {
    const ipx = stubIpx()

    useCanonicalIcon()('https://ddragon.leagueoflegends.com/cdn/16.15.1/img/champion/Annie.png')

    expect(ipx).toHaveBeenCalledWith(
      'https://ddragon.leagueoflegends.com/cdn/16.15.1/img/champion/Annie.png',
      { width: ICON_FETCH_SIZE, height: ICON_FETCH_SIZE, format: 'webp' },
    )
  })

  it('gives two different display sizes of one asset the same URL', () => {
    stubIpx()
    const canonicalIcon = useCanonicalIcon()
    const src = 'https://ddragon.leagueoflegends.com/cdn/16.15.1/img/item/3157.png'

    // The whole point: a 12 px lane glyph and a 64 px build slot must not
    // produce two cache entries. Callers cannot pass a size, so they cannot
    // diverge — this pins that they still cannot.
    expect(canonicalIcon(src)).toBe(canonicalIcon(src))
  })

  it('returns undefined for a missing src without calling through', () => {
    const ipx = stubIpx()
    const canonicalIcon = useCanonicalIcon()

    // Callers bind the result straight to `:src`; emitting `/_ipx/…/null`
    // would turn an absent icon into a 404 instead of an empty slot.
    expect(canonicalIcon(null)).toBeUndefined()
    expect(canonicalIcon(undefined)).toBeUndefined()
    expect(canonicalIcon('')).toBeUndefined()
    expect(ipx).not.toHaveBeenCalled()
  })
})

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

  it('binds every raw <img> to a URL builder rather than to an upstream URL', () => {
    // The regression the search palette shipped was `:src="champion.iconUrl"`
    // — a *variable* holding the upstream CDN URL, so the browser fetched a
    // 120×120 asset from Riot with no resize, no WebP and no cache of ours.
    // Matching CDN hostnames in the source would not have caught that (the
    // hostname is nowhere in the template), and neither would the useImage()
    // whitelist above (the component simply never called it). Allow-listing
    // the *expression* is what closes it: `champion.iconUrl` is none of these.
    const ALLOWED_SRC = [
      /^canonicalIcon\(/, // the shared helper
      /^optimizedSrc$/, // SkeletonImage's own computed, itself the helper
      /^ipx\(/, // RankIcon's documented SVG exception
    ]

    const offenders: string[] = []

    for (const rel of sourceFiles(APP_DIR)) {
      if (!rel.endsWith('.vue')) continue
      // Comments mention `<img>` prose in several components; stripping them
      // first keeps those out of the scan.
      const source = readFileSync(join(APP_DIR, rel), 'utf8').replace(/<!--[\s\S]*?-->/g, '')

      const tags = source.match(/<img\b[^>]*>/g) ?? []
      // Guards the parser itself: an attribute value containing `>` would end
      // the match early and silently shrink the scan.
      expect(tags.length, `unparsed <img> tag in ${rel}`).toBe((source.match(/<img\b/g) ?? []).length)

      for (const tag of tags) {
        // Both quote styles: nothing in web/ lints Vue templates, so a
        // single-quoted binding is reachable and would otherwise slip through.
        const match = /:src=(?:"([^"]*)"|'([^']*)')/.exec(tag)
        const src = match?.[1] ?? match?.[2]
        if (src && !ALLOWED_SRC.some(pattern => pattern.test(src.trim()))) {
          offenders.push(`${rel}: :src="${src}"`)
        }
      }
    }

    expect(offenders).toEqual([])
  })
})
