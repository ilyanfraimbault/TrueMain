import type { DesktopPlatform, DesktopRelease } from '~~/shared/types/desktop'

/**
 * The desktop companion's newest release (#1719), read from the repository's
 * GitHub releases. The app ships from `desktop-v*` tags as pre-releases
 * (`.github/workflows/desktop-release.yml`), beside the site's own releases, so
 * "latest" in GitHub's sense — the newest non-pre-release — is the site's and
 * never the app's: the list is filtered on the tag prefix instead.
 *
 * Read here rather than by the browser so every visitor and every installed
 * app shares one upstream call per TTL — GitHub allows sixty unauthenticated
 * calls an hour per address — and so a new app version needs no site deploy.
 */

export const DESKTOP_RELEASES_URL = 'https://api.github.com/repos/ilyanfraimbault/TrueMain/releases?per_page=30'
const TAG_PREFIX = 'desktop-v'

export interface GitHubAsset {
  name: string
  browser_download_url: string
}

export interface GitHubRelease {
  tag_name: string
  draft: boolean
  published_at: string | null
  html_url: string
  assets: GitHubAsset[]
}

const INSTALLER_SUFFIX: Record<DesktopPlatform, string> = {
  mac: '.dmg',
  windows: '-setup.exe',
}

/** The newest published `desktop-v*` release, shaped for the site; null when there is none yet. */
export function toDesktopRelease(releases: GitHubRelease[]): DesktopRelease | null {
  const newest = releases
    .filter(release => release.tag_name.startsWith(TAG_PREFIX) && !release.draft && release.published_at)
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
