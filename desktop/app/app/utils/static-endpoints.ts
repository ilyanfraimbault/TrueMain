import type { ChampionStaticListItem } from '#shared/types/static-data'
import { isLiveChampionId } from '#shared/utils/ddragon'
import { DDRAGON } from '~/utils/static-data'

/**
 * The site's `/api/static/*` routes (`web/server/api/static`), answered in the
 * webview from Data Dragon: the app has no server to run them, and the shared
 * pages ask for them by the site's paths. Each answer keeps the site's shape
 * and filtering. `undefined` means the path is not a static one, and the
 * caller sends it to TrueMain's API instead.
 */
const VERSIONS_URL = 'https://ddragon.leagueoflegends.com/api/versions.json'

let versions: Promise<string[]> | null = null

/** Data Dragon's version list, newest first — one request per launch, retried after a failure. */
function loadVersions(): Promise<string[]> {
  versions ??= $fetch<string[]>(VERSIONS_URL).catch((error: unknown) => {
    versions = null
    throw error
  })
  return versions
}

/** `16.19` → the newest `16.19.x` Data Dragon publishes; nothing asked, or nothing matching → the latest. */
async function resolveVersion(patch: unknown): Promise<string> {
  const list = await loadVersions()
  const asked = typeof patch === 'string' ? patch : ''
  return list.find(version => asked && (version === asked || version.startsWith(`${asked}.`))) ?? list[0]!
}

interface ChampionListResponse {
  data: Record<string, { key: string, name: string, image: { full: string } }>
}

async function championList(patch: unknown): Promise<ChampionStaticListItem[]> {
  const version = await resolveVersion(patch)
  const payload = await $fetch<ChampionListResponse>(`${DDRAGON}/${version}/data/en_US/champion.json`)
  return Object.values(payload.data)
    .map(champion => ({
      championId: Number(champion.key),
      name: champion.name,
      iconUrl: `${DDRAGON}/${version}/img/champion/${champion.image.full}`,
    }))
    .filter(item => isLiveChampionId(item.championId))
}

export async function answerStaticEndpoint(path: string, query: Record<string, unknown>): Promise<unknown> {
  switch (path) {
    case '/static/versions': return await loadVersions()
    case '/static/champions': return await championList(query.patch)
    default: return undefined
  }
}
