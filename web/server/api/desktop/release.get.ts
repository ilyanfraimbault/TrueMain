import type { DesktopRelease } from '~~/shared/types/desktop'
import { loadDesktopRelease } from '~~/server/utils/desktop-release-loader'

/**
 * The desktop app's release for the download page (#1719, the site's channel): version,
 * date and which installers it carries — or `null` before the first one. A
 * failed read of GitHub is a 503, so the page can say "try again" rather than
 * "there is no app".
 */
export default defineEventHandler(async (): Promise<DesktopRelease | null> => {
  try {
    return await loadDesktopRelease()
  }
  catch {
    throw createError({ statusCode: 503, statusMessage: 'Desktop releases unavailable' })
  }
})
