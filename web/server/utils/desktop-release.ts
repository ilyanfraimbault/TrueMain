import type { DesktopChannel, DesktopPlatform, DesktopRelease } from '~~/shared/types/desktop'

/**
 * The desktop companion's release for this site's channel (#1719, #1772), read
 * from the repository's GitHub releases. Every app version is built as a
 * `desktop-v*` pre-release (`.github/workflows/desktop-release.yml`), beside the
 * site's own releases, so "latest" in GitHub's sense is the site's and never the
 * app's: the list is filtered on the tag prefix instead.
 *
 * Two channels, set per environment by `NUXT_DESKTOP_CHANNEL`: `beta` (preprod)
 * serves the newest preprod build — one per app change merged to develop
 * (#1799) — and `stable` (production, and the default) the one release that is
 * not a pre-release: a version bump, made stable only once production runs what
 * the build reads (`.github/scripts/desktop-held.sh`) or by hand
 * (`.github/workflows/desktop-promote.yml`).
 *
 * Read here rather than by the browser so every visitor and every installed
 * app shares one upstream call per TTL — GitHub allows sixty unauthenticated
 * calls an hour per address — and so a new app version needs no site deploy.
 */

// The largest page GitHub serves. The site's releases and the betas push an older stable release down the list,
// so `findDesktopRelease` reads further pages until the channel has its release.
const PAGE_SIZE = 100
export const DESKTOP_RELEASES_URL = `https://api.github.com/repos/ilyanfraimbault/TrueMain/releases?per_page=${PAGE_SIZE}`
// GitHub lists the newest releases first: the newest beta is on the first page, the one stable release may be
// further down. Each page is an upstream call counted against GitHub's sixty an hour, hence a bound.
const MAX_PAGES = 5
const TAG_PREFIX = 'desktop-v'
const MANIFESTS: Record<DesktopChannel, string> = { stable: 'latest.json', beta: 'latest-beta.json' }

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

/**
 * Every version is built twice (`desktop-release.yml`), and each channel serves the build of its own site: an app
 * downloaded from preprod reads preprod, one downloaded from truemain.lol reads production. The file names say
 * which is which — the browser saves the asset under its name — so a preprod download carries its version
 * (`truemain-0.2.0.dmg`) and a production one does not (`truemain.dmg`). Each flavour has its own update manifest,
 * pointing at its own archives, so an update never moves an app from one site to the other.
 */
function desktopAssetNames(channel: DesktopChannel, version: string): { installers: Record<DesktopPlatform, string>, manifest: string } {
  const name = channel === 'beta' ? `truemain-${version}` : 'truemain'
  return {
    installers: { mac: `${name}.dmg`, windows: `${name}.exe` },
    manifest: MANIFESTS[channel],
  }
}

/**
 * The newest published `desktop-v*` release the channel may serve, shaped for the site; null when there is none yet.
 * Stable serves the release that is not a pre-release; beta, the newest that carries the preprod flavour's manifest —
 * a production build (`desktop-vX.Y.Z`, only `latest.json`) published after a beta never hides it from preprod.
 */
export function toDesktopRelease(releases: GitHubRelease[], channel: DesktopChannel): DesktopRelease | null {
  const newest = releases
    .filter(release => release.tag_name.startsWith(TAG_PREFIX) && !release.draft && release.published_at)
    .filter(release => channel === 'beta'
      ? release.assets.some(candidate => candidate.name === MANIFESTS.beta)
      : !release.prerelease)
    .sort((a, b) => Date.parse(b.published_at!) - Date.parse(a.published_at!))[0]
  if (!newest) return null

  const version = newest.tag_name.slice(TAG_PREFIX.length)
  const names = desktopAssetNames(channel, version)
  const asset = (name: string) => newest.assets.find(candidate => candidate.name === name)?.browser_download_url
  const installers: Partial<Record<DesktopPlatform, string>> = {}
  for (const platform of Object.keys(names.installers) as DesktopPlatform[]) {
    const url = asset(names.installers[platform])
    if (url) installers[platform] = url
  }

  return {
    channel,
    version,
    tag: newest.tag_name,
    publishedAt: newest.published_at!,
    pageUrl: newest.html_url,
    installers,
    manifestUrl: asset(names.manifest) ?? null,
  }
}

/** The channel's release, reading the release list a page at a time (1-based) until it is found or the list ends. */
export async function findDesktopRelease(
  fetchPage: (page: number) => Promise<GitHubRelease[]>,
  channel: DesktopChannel,
): Promise<DesktopRelease | null> {
  const releases: GitHubRelease[] = []
  for (let page = 1; page <= MAX_PAGES; page++) {
    const batch = await fetchPage(page)
    releases.push(...batch)
    const release = toDesktopRelease(releases, channel)
    if (release || batch.length < PAGE_SIZE) return release
  }
  return null
}
