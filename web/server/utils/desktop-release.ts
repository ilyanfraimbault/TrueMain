import type { DesktopPlatform, DesktopRelease } from '~~/shared/types/desktop'

/**
 * The desktop companion's release for this site's channel (#1719, #1772), read
 * from the repository's GitHub releases. Every app version is built as a
 * `desktop-v*` pre-release (`.github/workflows/desktop-release.yml`), beside the
 * site's own releases, so "latest" in GitHub's sense is the site's and never the
 * app's: the list is filtered on the tag prefix instead.
 *
 * Two channels, set per environment by `NUXT_DESKTOP_CHANNEL`: `beta` (preprod)
 * serves the newest build, pre-releases included; `stable` (production, and the
 * default) serves only a release promoted by hand
 * (`.github/workflows/desktop-promote.yml`), so a version bump reaches preprod on
 * merge and never reaches players before someone decides it should.
 *
 * Read here rather than by the browser so every visitor and every installed
 * app shares one upstream call per TTL — GitHub allows sixty unauthenticated
 * calls an hour per address — and so a new app version needs no site deploy.
 */

// The largest page GitHub serves: the site releases far more often than the app, and a desktop release pushed out
// of the page by site releases would read as "no app" on the download page and in the update feed.
export const DESKTOP_RELEASES_URL = 'https://api.github.com/repos/ilyanfraimbault/TrueMain/releases?per_page=100'
const TAG_PREFIX = 'desktop-v'

export type DesktopChannel = 'stable' | 'beta'

/** The channel an environment variable names; anything but `beta` is `stable`, so a typo never ships a pre-release. */
export function toDesktopChannel(value: unknown): DesktopChannel {
  return value === 'beta' ? 'beta' : 'stable'
}

export interface GitHubAsset {
  name: string
  browser_download_url: string
}

export interface GitHubRelease {
  tag_name: string
  draft: boolean
  prerelease: boolean
  published_at: string | null
  html_url: string
  assets: GitHubAsset[]
}

const INSTALLER_SUFFIX: Record<DesktopPlatform, string> = {
  mac: '.dmg',
  windows: '-setup.exe',
}

/** The newest published `desktop-v*` release the channel may serve, shaped for the site; null when there is none yet. */
export function toDesktopRelease(releases: GitHubRelease[], channel: DesktopChannel): DesktopRelease | null {
  const newest = releases
    .filter(release => release.tag_name.startsWith(TAG_PREFIX) && !release.draft && release.published_at)
    .filter(release => channel === 'beta' || !release.prerelease)
    .sort((a, b) => Date.parse(b.published_at!) - Date.parse(a.published_at!))[0]
  if (!newest) return null

  const asset = (suffix: string) => newest.assets.find(candidate => candidate.name.endsWith(suffix))?.browser_download_url
  const installers: Partial<Record<DesktopPlatform, string>> = {}
  for (const platform of Object.keys(INSTALLER_SUFFIX) as DesktopPlatform[]) {
    const url = asset(INSTALLER_SUFFIX[platform])
    if (url) installers[platform] = url
  }

  return {
    version: newest.tag_name.slice(TAG_PREFIX.length),
    tag: newest.tag_name,
    publishedAt: newest.published_at!,
    pageUrl: newest.html_url,
    installers,
    manifestUrl: asset('latest.json') ?? null,
  }
}
