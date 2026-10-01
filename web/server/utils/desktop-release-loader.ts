import type { DesktopRelease } from '~~/shared/types/desktop'
import type { GitHubRelease } from '~~/server/utils/desktop-release'
import { DESKTOP_RELEASES_URL, toDesktopChannel, toDesktopRelease } from '~~/server/utils/desktop-release'

/**
 * Cached five minutes: a release shows up on the site that fast, and a failed
 * read throws rather than caching "no release" — the page then says the
 * download is unavailable for a moment instead of that there is no app.
 */
export const loadDesktopRelease = defineCachedFunction(
  async (): Promise<DesktopRelease | null> => {
    const releases = await $fetch<GitHubRelease[]>(DESKTOP_RELEASES_URL, {
      headers: { accept: 'application/vnd.github+json', 'user-agent': 'truemain.lol' },
      timeout: 5000,
    })
    return toDesktopRelease(releases, toDesktopChannel(useRuntimeConfig().desktopChannel))
  },
  { name: 'desktop-release', maxAge: 5 * 60, getKey: () => toDesktopChannel(useRuntimeConfig().desktopChannel) },
)
