import type { ChampionStaticData, ChampionStaticListItem } from '#shared/types/static-data'
import { isLiveChampionId } from '#shared/utils/ddragon'
import type { ChampionDetailResponse, ItemListResponse, SummonerListResponse } from '~/utils/static-data'
import { DDRAGON, fetchRuneTree, toChampionStatic, toItemsMap, toSummonersMap } from '~/utils/static-data'

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
  data: Record<string, { id: string, key: string, name: string, image: { full: string } }>
}

const championLists = new Map<string, Promise<ChampionListResponse>>()

/** One version's `champion.json`, fetched once per launch: the list and the per-champion lookups share it. */
function loadChampionList(version: string): Promise<ChampionListResponse> {
  let list = championLists.get(version)
  if (!list) {
    list = $fetch<ChampionListResponse>(`${DDRAGON}/${version}/data/en_US/champion.json`).catch((error: unknown) => {
      championLists.delete(version)
      throw error
    })
    championLists.set(version, list)
  }
  return list
}

async function championList(patch: unknown): Promise<ChampionStaticListItem[]> {
  const version = await resolveVersion(patch)
  const payload = await loadChampionList(version)
  return Object.values(payload.data)
    .map(champion => ({
      championId: Number(champion.key),
      name: champion.name,
      iconUrl: `${DDRAGON}/${version}/img/champion/${champion.image.full}`,
    }))
    .filter(item => isLiveChampionId(item.championId))
}

const NO_CHAMPION_STATIC: ChampionStaticData = { championName: null, championIconUrl: null, championSpells: {}, partype: '' }

/** The site's `/static/{championId}`: one champion's name, icon, Q/W/E/R and resource. */
async function championStatic(championId: number, patch: unknown): Promise<ChampionStaticData> {
  const version = await resolveVersion(patch)
  const entry = Object.values((await loadChampionList(version)).data).find(champion => Number(champion.key) === championId)
  if (!entry) return NO_CHAMPION_STATIC
  const detail = await $fetch<ChampionDetailResponse>(`${DDRAGON}/${version}/data/en_US/champion/${entry.id}.json`)
  return toChampionStatic(detail, entry.id, entry.name, version)
}

async function itemMap(patch: unknown) {
  const version = await resolveVersion(patch)
  return toItemsMap(await $fetch<ItemListResponse>(`${DDRAGON}/${version}/data/en_US/item.json`), version)
}

async function summonerMap(patch: unknown) {
  const version = await resolveVersion(patch)
  return toSummonersMap(await $fetch<SummonerListResponse>(`${DDRAGON}/${version}/data/en_US/summoner.json`), version)
}

export async function answerStaticEndpoint(path: string, query: Record<string, unknown>): Promise<unknown> {
  switch (path) {
    case '/static/versions': return await loadVersions()
    case '/static/champions': return await championList(query.patch)
    case '/static/items': return await itemMap(query.patch)
    case '/static/summoner-spells': return await summonerMap(query.patch)
    case '/static/rune-tree': return await fetchRuneTree(typeof query.patch === 'string' && query.patch ? query.patch : null)
    default: {
      const championId = path.match(/^\/static\/(\d+)$/)?.[1]
      return championId ? await championStatic(Number(championId), query.patch) : undefined
    }
  }
}
