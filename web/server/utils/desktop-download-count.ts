import type { H3Event } from 'h3'
import type { DesktopPlatform } from '~~/shared/types/desktop'

/**
 * User agents that follow a link without anyone downloading anything: crawlers,
 * link previews (a pasted `/download` link unfurled in Discord or Slack) and
 * scripted clients. Not an exhaustive list — it keeps the obvious ones out of a
 * count that should mean "a person got the installer".
 */
const NOT_A_PERSON = /bot|crawl|spider|slurp|facebookexternalhit|embedly|preview|headless|curl|wget|python|go-http-client|okhttp|axios|node-fetch|undici/i

export function isAutomatedClient(userAgent: string | null | undefined): boolean {
  return !userAgent || NOT_A_PERSON.test(userAgent)
}

/**
 * Counts one installer download in the admin portal (#1805), through the API's
 * `POST /internal/desktop/downloads` — the key this server already holds for its
 * logs (`NUXT_LOG_INGEST_KEY`), off without it.
 *
 * Never in the visitor's way: the redirect does not wait for it, a failure is
 * dropped without a retry, and an automated client is not counted.
 */
export function countDesktopDownload(event: H3Event, platform: DesktopPlatform, version: string): void {
  const { apiBaseUrl, logIngestKey } = useRuntimeConfig(event)
  if (!logIngestKey || isAutomatedClient(getRequestHeader(event, 'user-agent'))) return

  const sent = $fetch(`${apiBaseUrl}/internal/desktop/downloads`, {
    method: 'POST',
    body: { platform, version },
    headers: { 'X-Log-Ingest-Key': logIngestKey },
    timeout: 3_000,
    retry: 0,
  }).catch((error: unknown) => {
    console.error('[desktop-download] could not count a download:', error instanceof Error ? error.message : error)
  })
  event.waitUntil?.(sent)
}
