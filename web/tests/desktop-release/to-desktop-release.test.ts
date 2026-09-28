import { describe, expect, it } from 'vitest'
import type { GitHubRelease } from '~~/server/utils/desktop-release'
import { toDesktopRelease } from '~~/server/utils/desktop-release'

const asset = (name: string) => ({ name, browser_download_url: `https://github.com/dl/${name}` })

const release = (tag: string, published: string | null, names: string[], draft = false): GitHubRelease => ({
  tag_name: tag,
  draft,
  published_at: published,
  html_url: `https://github.com/r/${tag}`,
  assets: names.map(asset),
})

describe('toDesktopRelease', () => {
  it('picks the newest desktop release, never the site\'s own', () => {
    const answer = toDesktopRelease([
      release('1.23.0', '2026-10-02T10:00:00Z', ['source.zip']),
      release('desktop-v0.1.1', '2026-10-01T10:00:00Z', ['TrueMain_0.1.1_universal.dmg', 'TrueMain_0.1.1_x64-setup.exe', 'latest.json']),
      release('desktop-v0.1.0', '2026-09-29T10:00:00Z', ['TrueMain_0.1.0_universal.dmg']),
    ])
    expect(answer).toEqual({
      version: '0.1.1',
      tag: 'desktop-v0.1.1',
      publishedAt: '2026-10-01T10:00:00Z',
      pageUrl: 'https://github.com/r/desktop-v0.1.1',
      installers: {
        mac: 'https://github.com/dl/TrueMain_0.1.1_universal.dmg',
        windows: 'https://github.com/dl/TrueMain_0.1.1_x64-setup.exe',
      },
      manifestUrl: 'https://github.com/dl/latest.json',
    })
  })

  it('skips drafts and unpublished releases', () => {
    const answer = toDesktopRelease([
      release('desktop-v0.2.0', null, ['a.dmg'], true),
      release('desktop-v0.1.0', '2026-09-29T10:00:00Z', ['TrueMain_0.1.0_universal.dmg']),
    ])
    expect(answer?.version).toBe('0.1.0')
  })

  it('leaves out a platform whose build is missing, and the manifest when there is none', () => {
    const answer = toDesktopRelease([release('desktop-v0.1.0', '2026-09-29T10:00:00Z', ['TrueMain_0.1.0_universal.dmg'])])
    expect(answer?.installers).toEqual({ mac: 'https://github.com/dl/TrueMain_0.1.0_universal.dmg' })
    expect(answer?.manifestUrl).toBeNull()
  })

  it('answers null before the first desktop release', () => {
    expect(toDesktopRelease([release('1.22.4', '2026-09-25T20:00:00Z', [])])).toBeNull()
  })
})
