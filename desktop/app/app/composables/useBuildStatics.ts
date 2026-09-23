/**
 * Icons and names for runes, summoner spells and items, straight from Data
 * Dragon — the same source as the champion list, fetched once per launch on
 * the patch `useChampionStatics` resolved.
 */
interface Named {
  name: string
  icon: string
}

const DDRAGON = 'https://ddragon.leagueoflegends.com/cdn'

/**
 * Stat shards are absent from Data Dragon's rune tree. Their icons come from
 * Community Dragon, keyed by perk id as its `perks.json` files them — two of
 * the health shards' file names are swapped there, which is why this is a
 * table and not a rule.
 */
const SHARD_BASE = 'https://raw.communitydragon.org/latest/plugins/rcp-be-lol-game-data/global/default/v1/perk-images/statmods'
const SHARDS: Record<number, Named> = {
  5008: { name: 'Adaptive Force', icon: 'statmodsadaptiveforceicon' },
  5005: { name: 'Attack Speed', icon: 'statmodsattackspeedicon' },
  5007: { name: 'Ability Haste', icon: 'statmodscdrscalingicon' },
  5010: { name: 'Move Speed', icon: 'statmodsmovementspeedicon' },
  5001: { name: 'Health Scaling', icon: 'statmodshealthplusicon' },
  5011: { name: 'Health', icon: 'statmodshealthscalingicon' },
  5013: { name: 'Tenacity and Slow Resist', icon: 'statmodstenacityicon' },
  5002: { name: 'Armor', icon: 'statmodsarmoricon' },
  5003: { name: 'Magic Resist', icon: 'statmodsmagicresicon' },
  5012: { name: 'Resist Scaling', icon: 'statmodsadaptiveforcescalingicon' },
}

interface RuneStyle { id: number, name: string, icon: string, slots: { runes: { id: number, name: string, icon: string }[] }[] }

/** A rune tree as the core view lays it out: the keystone row, then three rows of three. */
export interface RuneTreeStyle {
  keystones: number[]
  subRows: number[][]
}

/** The three shard rows, top to bottom — offense, flex, defense. */
export const SHARD_SLOTS: number[][] = [
  [5008, 5005, 5007],
  [5008, 5010, 5001],
  [5011, 5013, 5001],
]

export function useBuildStatics() {
  const { patch } = useChampionStatics()
  const perks = useState<Record<number, Named>>('ddragon-perks', () => ({}))
  const styles = useState<Record<number, RuneTreeStyle>>('ddragon-rune-styles', () => ({}))
  const championSpells = useState<Record<string, Named[]>>('ddragon-champion-spells', () => ({}))
  const spells = useState<Record<number, Named>>('ddragon-spells', () => ({}))
  const items = useState<Record<number, string>>('ddragon-items', () => ({}))
  const loadedFor = useState<string>('ddragon-build-patch', () => '')

  async function load(version: string) {
    if (!version || loadedFor.value === version) return
    loadedFor.value = version
    try {
      const [runes, summoners, itemData] = await Promise.all([
        $fetch<RuneStyle[]>(`${DDRAGON}/${version}/data/en_GB/runesReforged.json`),
        $fetch<{ data: Record<string, { key: string, name: string, image: { full: string } }> }>(
          `${DDRAGON}/${version}/data/en_GB/summoner.json`,
        ),
        $fetch<{ data: Record<string, { name: string }> }>(`${DDRAGON}/${version}/data/en_GB/item.json`),
      ])

      const nextPerks: Record<number, Named> = {}
      const nextStyles: Record<number, RuneTreeStyle> = {}
      for (const style of runes) {
        nextPerks[style.id] = { name: style.name, icon: `${DDRAGON}/img/${style.icon}` }
        const [keystones, ...rows] = style.slots.map(slot => slot.runes.map(rune => rune.id))
        nextStyles[style.id] = { keystones: keystones ?? [], subRows: rows }
        for (const slot of style.slots) {
          for (const rune of slot.runes) nextPerks[rune.id] = { name: rune.name, icon: `${DDRAGON}/img/${rune.icon}` }
        }
      }
      perks.value = nextPerks
      styles.value = nextStyles

      const nextSpells: Record<number, Named> = {}
      for (const spell of Object.values(summoners.data)) {
        nextSpells[Number(spell.key)] = { name: spell.name, icon: `${DDRAGON}/${version}/img/spell/${spell.image.full}` }
      }
      spells.value = nextSpells

      items.value = Object.fromEntries(Object.entries(itemData.data).map(([id, item]) => [id, item.name]))
    }
    catch {
      // Offline: the panel draws empty icon slots rather than failing.
      loadedFor.value = ''
    }
  }

  watch(patch, load, { immediate: true })

  const perk = (id: number): Named | null => {
    const shard = SHARDS[id]
    if (shard) return { name: shard.name, icon: `${SHARD_BASE}/${shard.icon}.png` }
    return perks.value[id] ?? null
  }
  const runeStyle = (id: number): RuneTreeStyle | null => styles.value[id] ?? null

  /** Q, W, E, R of one champion, fetched the first time it is asked for. */
  async function loadChampionSpells(alias: string) {
    const version = patch.value
    if (!version || championSpells.value[alias]) return
    try {
      const payload = await $fetch<{ data: Record<string, { spells: { name: string, image: { full: string } }[] }> }>(
        `${DDRAGON}/${version}/data/en_GB/champion/${alias}.json`,
      )
      const spellList = payload.data[alias]?.spells ?? []
      championSpells.value = {
        ...championSpells.value,
        [alias]: spellList.map(entry => ({ name: entry.name, icon: `${DDRAGON}/${version}/img/spell/${entry.image.full}` })),
      }
    }
    catch {
      // The skill order falls back to its letters.
    }
  }
  const championSpell = (alias: string, key: string): Named | null =>
    championSpells.value[alias]?.['QWER'.indexOf(key)] ?? null

  const spell = (id: number): Named | null => spells.value[id] ?? null
  const item = (id: number): Named | null =>
    patch.value ? { name: items.value[id] ?? '', icon: `${DDRAGON}/${patch.value}/img/item/${id}.png` } : null

  return { perk, runeStyle, spell, item, loadChampionSpells, championSpell }
}
