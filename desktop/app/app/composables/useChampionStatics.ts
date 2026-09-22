import type { Ref } from 'vue'

export interface Champion {
  id: number
  /** Riot's alias, e.g. `MonkeyKing` — the key its art is filed under. */
  alias: string
  name: string
}

/**
 * Champion identities, straight from Data Dragon.
 *
 * The site reaches these through its Nitro proxy; the desktop app has no
 * server, so it talks to Data Dragon directly. Fetched once per launch and
 * held for the session — the set only changes on a patch, and a champ select
 * is not the moment to discover a cache miss.
 */
export function useChampionStatics() {
  const champions = useState<Map<number, Champion>>('ddragon-champions', () => new Map())
  const patch = useState<string>('ddragon-patch', () => '')
  const loaded = useState<boolean>('ddragon-loaded', () => false)

  async function load() {
    if (loaded.value) return
    try {
      const versions = await $fetch<string[]>('https://ddragon.leagueoflegends.com/api/versions.json')
      const latest = versions[0]
      if (!latest) return

      const payload = await $fetch<{ data: Record<string, { key: string, id: string, name: string }> }>(
        `https://ddragon.leagueoflegends.com/cdn/${latest}/data/en_GB/champion.json`,
      )

      const next = new Map<number, Champion>()
      for (const entry of Object.values(payload.data)) {
        next.set(Number(entry.key), { id: Number(entry.key), alias: entry.id, name: entry.name })
      }
      champions.value = next
      patch.value = latest
      loaded.value = true
    }
    catch {
      // Offline, or Data Dragon is down. The draft still works on ids — the
      // panel degrades to numbers rather than failing to render.
      loaded.value = false
    }
  }

  onMounted(load)

  const nameOf = (id: number) => champions.value.get(id)?.name ?? `Champion ${id}`

  const portraitOf = (id: number): string | null => {
    const champion = champions.value.get(id)
    if (!champion || !patch.value) return null
    return `https://ddragon.leagueoflegends.com/cdn/${patch.value}/img/champion/${champion.alias}.png`
  }

  return { champions: champions as Ref<Map<number, Champion>>, patch, loaded, nameOf, portraitOf }
}
