import type { DesktopPlatform } from '~~/shared/types/desktop'
import { loadDesktopRelease } from '~~/server/utils/desktop-release-loader'
import { countDesktopDownload } from '~~/server/utils/desktop-download-count'

const PLATFORMS: DesktopPlatform[] = ['mac', 'windows']

/**
 * A stable download link per platform (#1719) — `/api/desktop/download/mac`,
 * `/api/desktop/download/windows` — redirecting to the channel's release's
 * installer, so the site's buttons never name a version. Each one followed is
 * counted for the admin portal (#1805).
 */
export default defineEventHandler(async (event) => {
  const platform = getRouterParam(event, 'platform') as DesktopPlatform
  if (!PLATFORMS.includes(platform)) {
    throw createError({ statusCode: 404, statusMessage: 'Unknown platform' })
  }
  const release = await loadDesktopRelease().catch(() => {
    throw createError({ statusCode: 503, statusMessage: 'Desktop releases unavailable' })
  })
  const url = release?.installers[platform]
  if (!release || !url) {
    throw createError({ statusCode: 404, statusMessage: 'No installer for this platform yet' })
  }
  countDesktopDownload(event, platform, release.version)
  return sendRedirect(event, url, 302)
})
