import { describe, expect, it } from 'vitest'
import {
  buildChampionIdBySlug,
  championPath,
  championRouteAction,
  championSegment,
  resolveChampionParam,
  truemainChampionPath,
} from '~~/shared/utils/champion-slug'
import { toChampionSlug } from '~~/shared/utils/ddragon'

/**
 * Champion URL slugs (#1124). Three places have to agree on a champion's URL —
 * the router, every link builder, and the sitemap — and none of them fails
 * loudly when they don't: a mismatch is a page that renders fine under a URL
 * the sitemap never advertises, or an internal link that 301s on every click.
 * These tests pin the agreement.
 */

const SLUGS = {
  103: 'ahri',
  11: 'masteryi',
  1: 'annie',
}

const ID_BY_SLUG = buildChampionIdBySlug(SLUGS)

describe('toChampionSlug', () => {
  it('lower-cases the DDragon key and nothing else', () => {
    expect(toChampionSlug('Ahri')).toBe('ahri')
    expect(toChampionSlug('MasterYi')).toBe('masteryi')
    // Keys with no separator stay unseparated — deriving from the *display*
    // name ("Master Yi", "Nunu & Willump") is the lossy alternative this
    // deliberately avoids.
    expect(toChampionSlug('MonkeyKing')).toBe('monkeyking')
  })
})

describe('championSegment / path builders', () => {
  it('builds slug paths for both routes', () => {
    expect(championPath(103, SLUGS)).toBe('/champions/ahri')
    expect(truemainChampionPath('Faker#KR1', 11, SLUGS))
      .toBe('/truemains/Faker%23KR1/champions/masteryi')
  })

  it('falls back to the numeric id rather than emitting a dead URL', () => {
    // The map is best-effort (a DDragon outage empties it) and can lag a brand
    // new champion. A numeric link still reaches the page and gets redirected;
    // `/champions/undefined` would not.
    expect(championSegment(103, null)).toBe('103')
    expect(championPath(999, SLUGS)).toBe('/champions/999')
  })
})

describe('resolveChampionParam', () => {
  it('reads the canonical slug and asks for no redirect', () => {
    const resolved = resolveChampionParam('ahri', SLUGS, ID_BY_SLUG)
    expect(resolved.championId).toBe(103)
    expect(resolved.canonicalSegment).toBe('ahri')
  })

  it('resolves the legacy numeric id and points at the slug', () => {
    // Every link minted before #1124 and every external backlink is this shape.
    const resolved = resolveChampionParam('103', SLUGS, ID_BY_SLUG)
    expect(resolved.championId).toBe(103)
    expect(resolved.canonicalSegment).toBe('ahri')
  })

  it('resolves a mis-cased slug so case cannot fork one page into two URLs', () => {
    const resolved = resolveChampionParam('Ahri', SLUGS, ID_BY_SLUG)
    expect(resolved.championId).toBe(103)
    expect(resolved.canonicalSegment).toBe('ahri')
  })

  it('gives an unmapped numeric id itself as its canonical segment', () => {
    // Otherwise a champion DDragon has not listed yet would redirect-loop:
    // "you are at 999, go to 999".
    const resolved = resolveChampionParam('999', SLUGS, ID_BY_SLUG)
    expect(resolved.championId).toBe(999)
    expect(resolved.canonicalSegment).toBe('999')
  })

  it('rejects anything that names no champion', () => {
    for (const segment of ['nonsense', '', '0', '-1', '99999999']) {
      expect(resolveChampionParam(segment, SLUGS, ID_BY_SLUG).championId).toBeNull()
    }
  })

  it('still resolves numeric ids when the slug map never loaded', () => {
    const empty = buildChampionIdBySlug(null)
    expect(resolveChampionParam('103', null, empty).championId).toBe(103)
    expect(resolveChampionParam('ahri', null, empty).championId).toBeNull()
  })
})

describe('championRouteAction', () => {
  it('renders the canonical URL without redirecting', () => {
    expect(championRouteAction('ahri', SLUGS, ID_BY_SLUG))
      .toEqual({ type: 'render', championId: 103 })
  })

  it('redirects the legacy numeric id, a mis-cased slug and a padded id', () => {
    // All three render the same page, so all three have to consolidate onto one
    // URL. Leading zeros are the easy one to miss — `Number('0103')` is 103, so
    // without the segment comparison this would silently serve a second URL.
    for (const segment of ['103', 'Ahri', 'AHRI', '0103']) {
      expect(championRouteAction(segment, SLUGS, ID_BY_SLUG))
        .toEqual({ type: 'redirect', championId: 103, segment: 'ahri' })
    }
  })

  it('404s an unknown segment when the roster is known', () => {
    expect(championRouteAction('nonsense', SLUGS, ID_BY_SLUG)).toEqual({ type: 'notFound' })
  })

  it('503s instead of 404ing a real slug while the map is empty', () => {
    // The regression this guards: during a DDragon outage with a cold cache,
    // `/champions/ahri` — canonical and indexed — is indistinguishable from a
    // typo. Answering 404 asks search engines to drop the URL over something
    // transient and self-healing; 503 asks them to come back.
    const empty = buildChampionIdBySlug(null)
    expect(championRouteAction('ahri', null, empty)).toEqual({ type: 'unavailable' })
    expect(championRouteAction('nonsense', null, empty)).toEqual({ type: 'unavailable' })
    // Numeric ids need no map, so they still resolve and still redirect.
    expect(championRouteAction('103', null, empty))
      .toEqual({ type: 'render', championId: 103 })
  })
})
