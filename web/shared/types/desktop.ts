/** The desktop companion's platforms with an installer (#1719). */
export type DesktopPlatform = 'mac' | 'windows'

/** The newest desktop release, as `GET /api/desktop/release` answers it. */
export interface DesktopRelease {
  version: string
  tag: string
  publishedAt: string
  pageUrl: string
  /** Installer download URLs, by platform; absent when that build failed. */
  installers: Partial<Record<DesktopPlatform, string>>
  /** The updater manifest (`latest.json`) the app polls, when the release carries one. */
  manifestUrl: string | null
}

