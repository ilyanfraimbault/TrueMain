import { describe, expect, it } from 'vitest'
import type { GitHubRelease } from '~~/server/utils/desktop-release'
import { toDesktopChannel, toDesktopRelease } from '~~/server/utils/desktop-release'

const asset = (name: string) => ({ name, browser_download_url: `https://github.com/dl/${name}` })

// What `desktop-release.yml` publishes for a version: the production flavour and the preprod one.
const build = (version: string) => ['truemain.dmg', 'truemain.exe', 'latest.json', `truemain-${version}.dmg`, `truemain-${version}.exe`, 'latest-beta.json']

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
    const productionOnly = release('desktop-v0.1.1', '2026-10-01T10:00:00Z', ['truemain.dmg', 'truemain.exe', 'latest.json'])
    expect(toDesktopRelease([productionOnly], 'beta')).toMatchObject({ installers: {}, manifestUrl: null })
    const preprodOnly = release('desktop-v0.1.1', '2026-10-01T10:00:00Z', ['truemain-0.1.1.dmg', 'truemain-0.1.1.exe', 'latest-beta.json'])
    expect(toDesktopRelease([preprodOnly], 'stable')).toMatchObject({ installers: {}, manifestUrl: null })
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
