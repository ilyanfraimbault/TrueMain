/** The desktop companion's platforms with an installer (#1719). */
export type DesktopPlatform = 'mac' | 'windows'

/**
 * Which builds a site offers (#1772): `stable` (production) the promoted one, built against truemain.lol; `beta`
 * (preprod) the newest one, built against preprod.
 */
export type DesktopChannel = 'stable' | 'beta'

/** The desktop release this site's channel serves, as `GET /api/desktop/release` answers it. */
export interface DesktopRelease {
  channel: DesktopChannel
  version: string
  tag: string
  publishedAt: string
  pageUrl: string
  /** Installer download URLs, by platform; absent when that build failed. */
  installers: Partial<Record<DesktopPlatform, string>>
  /** The updater manifest (`latest.json`) the app polls, when the release carries one. */
  manifestUrl: string | null
}

