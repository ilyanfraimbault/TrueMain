import type { DesktopRelease } from '~~/shared/types/desktop'
import type { GitHubRelease } from '~~/server/utils/desktop-release'
import { DESKTOP_RELEASES_URL, findDesktopRelease, toDesktopChannel } from '~~/server/utils/desktop-release'

/**
 * Cached five minutes: a release shows up on the site that fast, and a failed
 * read throws rather than caching "no release" — the page then says the
 * download is unavailable for a moment instead of that there is no app.
 */
export const loadDesktopRelease = defineCachedFunction(
  async (): Promise<DesktopRelease | null> => findDesktopRelease(
    page => $fetch<GitHubRelease[]>(`${DESKTOP_RELEASES_URL}&page=${page}`, {
      headers: { accept: 'application/vnd.github+json', 'user-agent': 'truemain.lol' },
      timeout: 5000,
    }),
    toDesktopChannel(useRuntimeConfig().desktopChannel),
  ),
  { name: 'desktop-release', maxAge: 5 * 60, getKey: () => toDesktopChannel(useRuntimeConfig().desktopChannel) },
)
