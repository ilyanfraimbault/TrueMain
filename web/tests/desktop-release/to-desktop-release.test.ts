import { describe, expect, it } from 'vitest'
import type { GitHubRelease } from '~~/server/utils/desktop-release'
import { findDesktopRelease, toDesktopChannel, toDesktopRelease } from '~~/server/utils/desktop-release'

const asset = (name: string) => ({ name, browser_download_url: `https://github.com/dl/${name}` })

// Before #1799 a version carried both flavours; since, a bump publishes the production flavour (`desktop-vX.Y.Z`) and
// every app change the preprod one (`desktop-vX.Y.Z-beta.N`).
const build = (version: string) => ['truemain.dmg', 'truemain.exe', 'latest.json', `truemain-${version}.dmg`, `truemain-${version}.exe`, 'latest-beta.json']
const production = ['truemain.dmg', 'truemain.exe', 'latest.json']
const beta = (version: string) => [`truemain-${version}.dmg`, `truemain-${version}.exe`, 'latest-beta.json']

const release = (tag: string, published: string | null, names: string[], draft = false, prerelease = false): GitHubRelease => ({
  tag_name: tag,
  draft,
  prerelease,
  published_at: published,
  html_url: `https://github.com/r/${tag}`,
  assets: names.map(asset),
})

describe('toDesktopRelease', () => {
  it('picks the newest desktop release, never the site\'s own', () => {
    const answer = toDesktopRelease([
      release('1.23.0', '2026-10-02T10:00:00Z', ['source.zip']),
      release('desktop-v0.1.1', '2026-10-01T10:00:00Z', build('0.1.1')),
      release('desktop-v0.1.0', '2026-09-29T10:00:00Z', build('0.1.0')),
    ], 'stable')
    expect(answer).toEqual({
      channel: 'stable',
      version: '0.1.1',
      tag: 'desktop-v0.1.1',
      publishedAt: '2026-10-01T10:00:00Z',
      pageUrl: 'https://github.com/r/desktop-v0.1.1',
      installers: {
        mac: 'https://github.com/dl/truemain.dmg',
        windows: 'https://github.com/dl/truemain.exe',
      },
      manifestUrl: 'https://github.com/dl/latest.json',
    })
  })

  it('serves preprod its own flavour, named after its version', () => {
    const answer = toDesktopRelease([release('desktop-v0.1.1', '2026-10-01T10:00:00Z', build('0.1.1'), false, true)], 'beta')
    expect(answer?.installers).toEqual({
      mac: 'https://github.com/dl/truemain-0.1.1.dmg',
      windows: 'https://github.com/dl/truemain-0.1.1.exe',
    })
    expect(answer?.manifestUrl).toBe('https://github.com/dl/latest-beta.json')
  })

  it('never serves one channel the other\'s flavour', () => {
    const productionOnly = release('desktop-v0.1.1', '2026-10-01T10:00:00Z', production)
    expect(toDesktopRelease([productionOnly], 'beta')).toBeNull()
    const preprodOnly = release('desktop-v0.1.1', '2026-10-01T10:00:00Z', beta('0.1.1'))
    expect(toDesktopRelease([preprodOnly], 'stable')).toMatchObject({ installers: {}, manifestUrl: null })
  })

  it('serves preprod a beta build under its full version', () => {
    const answer = toDesktopRelease([release('desktop-v0.3.2-beta.58', '2026-10-02T10:00:00Z', beta('0.3.2-beta.58'), false, true)], 'beta')
    expect(answer).toMatchObject({
      version: '0.3.2-beta.58',
      installers: { mac: 'https://github.com/dl/truemain-0.3.2-beta.58.dmg', windows: 'https://github.com/dl/truemain-0.3.2-beta.58.exe' },
      manifestUrl: 'https://github.com/dl/latest-beta.json',
    })
  })

  it('keeps serving preprod its newest beta when a production build is published after it', () => {
    const answer = toDesktopRelease([
      release('desktop-v0.4.0', '2026-10-02T11:00:00Z', production),
      release('desktop-v0.4.0-beta.60', '2026-10-02T10:00:00Z', beta('0.4.0-beta.60'), false, true),
    ], 'beta')
    expect(answer?.version).toBe('0.4.0-beta.60')
  })

  it('skips drafts and unpublished releases', () => {
    const answer = toDesktopRelease([
      release('desktop-v0.2.0', null, ['a.dmg'], true),
      release('desktop-v0.1.0', '2026-09-29T10:00:00Z', build('0.1.0')),
    ], 'beta')
    expect(answer?.version).toBe('0.1.0')
  })

  it('leaves out a platform whose build is missing, and the manifest when there is none', () => {
    const answer = toDesktopRelease([release('desktop-v0.1.0', '2026-09-29T10:00:00Z', ['truemain.dmg'])], 'stable')
    expect(answer?.installers).toEqual({ mac: 'https://github.com/dl/truemain.dmg' })
    expect(answer?.manifestUrl).toBeNull()
  })

  it('answers null before the first desktop release', () => {
    expect(toDesktopRelease([release('1.22.4', '2026-09-25T20:00:00Z', [])], 'beta')).toBeNull()
  })

  describe('channels', () => {
    const releases = [
      release('desktop-v0.3.0', '2026-10-03T10:00:00Z', build('0.3.0'), false, true),
      release('desktop-v0.2.0', '2026-10-02T10:00:00Z', build('0.2.0')),
      release('desktop-v0.1.0', '2026-10-01T10:00:00Z', build('0.1.0')),
    ]

    it('serves the newest build on beta, pre-releases included', () => {
      expect(toDesktopRelease(releases, 'beta')?.version).toBe('0.3.0')
    })

    it('serves only a promoted release on stable', () => {
      expect(toDesktopRelease(releases, 'stable')?.version).toBe('0.2.0')
    })

    it('serves neither channel a production build still held back', () => {
      const held = release('desktop-v0.5.0', '2026-10-04T10:00:00Z', production, true)
      expect(toDesktopRelease([held, ...releases], 'stable')?.version).toBe('0.2.0')
      expect(toDesktopRelease([held, ...releases], 'beta')?.version).toBe('0.3.0')
    })

    it('answers null on stable until a build is promoted', () => {
      expect(toDesktopRelease([releases[0]!], 'stable')).toBeNull()
    })

    it('reads anything but beta as stable', () => {
      expect(toDesktopChannel('beta')).toBe('beta')
      expect(toDesktopChannel('stable')).toBe('stable')
      expect(toDesktopChannel('Beta')).toBe('stable')
      expect(toDesktopChannel(undefined)).toBe('stable')
    })
  })
})

describe('findDesktopRelease', () => {
  const siteReleases = (count: number, from = 0) => Array.from({ length: count }, (_, i) => release(`1.${from + i}.0`, '2026-09-01T10:00:00Z', []))

  it('reads further pages when site releases and betas push the stable one down', async () => {
    const pages = [
      [release('desktop-v0.4.1-beta.70', '2026-10-05T10:00:00Z', beta('0.4.1-beta.70'), false, true), ...siteReleases(99)],
      [...siteReleases(50, 99), release('desktop-v0.4.0', '2026-09-20T10:00:00Z', production)],
    ]
    const read: number[] = []
    const answer = await findDesktopRelease(async (page) => {
      read.push(page)
      return pages[page - 1] ?? []
    }, 'stable')
    expect(answer?.version).toBe('0.4.0')
    expect(read).toEqual([1, 2])
  })

  it('stops at the first page that has the channel\'s release', async () => {
    const read: number[] = []
    const answer = await findDesktopRelease(async (page) => {
      read.push(page)
      return [release('desktop-v0.4.1-beta.70', '2026-10-05T10:00:00Z', beta('0.4.1-beta.70'), false, true), ...siteReleases(99)]
    }, 'beta')
    expect(answer?.version).toBe('0.4.1-beta.70')
    expect(read).toEqual([1])
  })

  it('answers null at the end of the list, and gives up after five full pages', async () => {
    expect(await findDesktopRelease(async () => siteReleases(3), 'stable')).toBeNull()
    const read: number[] = []
    expect(await findDesktopRelease(async (page) => {
      read.push(page)
      return siteReleases(100)
    }, 'stable')).toBeNull()
    expect(read).toEqual([1, 2, 3, 4, 5])
  })
})
