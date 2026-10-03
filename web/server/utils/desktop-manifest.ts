/**
 * A release's signed update manifest (`latest.json` / `latest-beta.json`, written by `desktop-release.yml`), read
 * as JSON whatever the content type. GitHub serves release assets as `application/octet-stream`, which `$fetch`
 * hands back as a Blob: relayed fresh it streamed the manifest, but cached it serialised as `{}`, a manifest the
 * updater rejects — so no installed beta was offered its updates (#1789).
 */
export async function fetchUpdateManifest(url: string, fetcher: typeof fetch = fetch): Promise<Record<string, unknown>> {
  const response = await fetcher(url, { signal: AbortSignal.timeout(5000) })
  if (!response.ok) throw new Error(`Update manifest answered ${response.status}`)
  return await response.json() as Record<string, unknown>
}
