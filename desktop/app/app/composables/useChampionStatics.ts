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

  /**
   * The wide splash, for a band the text sits over. Filed by alias and not by
   * patch, so it needs the champion list but not the version.
   */
  const splashOf = (id: number): string | null => {
    const champion = champions.value.get(id)
    return champion ? splashOfAlias(champion.alias) : null
  }

  /** The tall "loading screen" art — the shape of a pick card. */
  const loadingOf = (id: number): string | null => {
    const champion = champions.value.get(id)
    return champion ? loadingOfAlias(champion.alias) : null
  }

  return {
    champions: champions as Ref<Map<number, Champion>>,
    patch,
    loaded,
    nameOf,
    portraitOf,
    splashOf,
    loadingOf,
  }
}

export const splashOfAlias = (alias: string) =>
  `https://ddragon.leagueoflegends.com/cdn/img/champion/splash/${alias}_0.jpg`

export const loadingOfAlias = (alias: string) =>
  `https://ddragon.leagueoflegends.com/cdn/img/champion/loading/${alias}_0.jpg`

/**
 * The art behind the screens that have no champion of their own — waiting for
 * the client, the lobby. A short list of splashes that read well dimmed and
 * cropped, rotated by the day so the app does not open on the same picture
 * forever. Nothing else is derived from it.
 */
const BACKDROPS = ['Ahri', 'Aatrox', 'Kaisa', 'Yone', 'Leona', 'Jinx', 'Sylas', 'Senna']

export function backdropAlias(): string {
  const day = Math.floor(Date.now() / 86_400_000)
  return BACKDROPS[day % BACKDROPS.length] ?? 'Ahri'
}
