import { fetchUpdateManifest } from '~~/server/utils/desktop-manifest'
import { loadDesktopRelease } from '~~/server/utils/desktop-release-loader'

/** One manifest per release URL, kept as long as the release list itself. */
const loadManifest = defineCachedFunction(
  (url: string) => fetchUpdateManifest(url),
  { name: 'desktop-update-manifest', maxAge: 5 * 60, getKey: (url: string) => url },
)

/**
 * The installed app's update feed (#1719, `plugins.updater.endpoints` in
 * `desktop/src-tauri/tauri.conf.json`): the channel's release's signed manifest,
 * relayed. No release yet — or no manifest on it — is a 204, which the updater
 * reads as "up to date"; a failed read of GitHub is a 503 it shrugs off until
 * the next launch.
 */
export default defineEventHandler(async (event) => {
  const release = await loadDesktopRelease().catch(() => {
    throw createError({ statusCode: 503, statusMessage: 'Desktop releases unavailable' })
  })
  if (!release?.manifestUrl) {
    setResponseStatus(event, 204)
    return null
  }
  return await loadManifest(release.manifestUrl).catch(() => {
    throw createError({ statusCode: 503, statusMessage: 'Update manifest unavailable' })
  })
})
