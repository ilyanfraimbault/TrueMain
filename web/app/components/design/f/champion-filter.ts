/**
 * Variant F ("Dock") — the home field's champion filter: local, instant, no API.
 *
 * A champion matches when its name starts with the term (best), when one of its
 * words or its initials does ("fortune", "mf"), when a common nickname does
 * ("j4", "ww"), or — from two letters on — when the term appears anywhere in
 * the name. Punctuation, spaces and accents are ignored on both sides, so
 * "kogmaw", "kog maw" and "Kog'Maw" are one name.
 */

export interface WallChampion {
  id: number
  name: string
  iconUrl: string
  /** The name folded: lowercase, accents and punctuation dropped. */
  key: string
  words: string[]
  initials: string
}

/** Nicknames players type, by the champion's folded name. */
const NICKNAMES: Record<string, string[]> = {
  jarvaniv: ['j4', 'jarvan'],
  warwick: ['ww'],
  gangplank: ['gp'],
  leblanc: ['lb'],
  aurelionsol: ['asol'],
  twistedfate: ['tf'],
  missfortune: ['mf'],
  tahmkench: ['tk'],
  kogmaw: ['kog'],
  chogath: ['cho'],
  heimerdinger: ['heimer', 'donger'],
  mordekaiser: ['morde'],
  volibear: ['voli'],
  nunuwillump: ['nunu'],
  masteryi: ['yi'],
  xinzhao: ['xin'],
  reksai: ['rek'],
  belveth: ['bel'],
  kaisa: ['kai'],
  velkoz: ['vel'],
  khazix: ['kha', 'k6'],
}

export function fold(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]/g, '')
}

export function toWallChampion(champion: { championId: number, name: string, iconUrl: string }): WallChampion {
  const words = champion.name
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()
    .split(/[^a-z0-9]+/)
    .filter(Boolean)
  return {
    id: champion.championId,
    name: champion.name,
    iconUrl: champion.iconUrl,
    key: fold(champion.name),
    words,
    initials: words.length > 1 ? words.map(word => word[0]).join('') : '',
  }
}

/** 0 = name prefix, 1 = word / initials / nickname, 2 = anywhere; null = no match. */
function score(champion: WallChampion, term: string): number | null {
  if (champion.key.startsWith(term)) return 0
  if (champion.words.some(word => word.startsWith(term))) return 1
  if (champion.initials && champion.initials === term) return 1
  if (NICKNAMES[champion.key]?.some(nick => nick.startsWith(term))) return 1
  if (term.length >= 2 && champion.key.includes(term)) return 2
  return null
}

/** The ids to show for a term, best matches first, alphabetical within a rank. */
export function matchChampions(champions: WallChampion[], rawTerm: string): number[] {
  const term = fold(rawTerm)
  if (!term) return champions.map(champion => champion.id)
  const ranked: [number, number, number][] = []
  champions.forEach((champion, index) => {
    const rank = score(champion, term)
    if (rank !== null) ranked.push([rank, index, champion.id])
  })
  ranked.sort((a, b) => a[0] - b[0] || a[1] - b[1])
  return ranked.map(entry => entry[2])
}

// Nuxt registers every file under `components/` as a component; a default export
// keeps that registration well-typed. Import the named functions.
export default matchChampions
