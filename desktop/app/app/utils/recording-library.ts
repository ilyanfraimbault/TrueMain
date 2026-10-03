import type { Clip, GameRecording, RecordingLibrary, RecordingResolution } from '~/types/recordings'

/**
 * The Recordings page's list: full games and clips as one kind of card, the
 * filters of its toolbar and the groups it is cut into.
 */

export interface LibraryItem {
  kind: 'game' | 'clip'
  id: string
  gameId: number
  queueId: number
  championId: number | null
  win: boolean | null
  kills: number | null
  deaths: number | null
  assists: number | null
  /** A clip's own title; a full game reads as the game, so the card builds its title. */
  title: string | null
  /** When the game was played: what a card's "how long ago" and its day are measured from. */
  atMs: number
  durationMs: number | null
  /** A favourite clip, a kept full game. */
  pinned: boolean
  status: GameRecording['status']
  videoPath: string
  thumbnailPath: string | null
  sizeBytes: number
}

export const gameItem = (game: GameRecording): LibraryItem => ({
  kind: 'game',
  id: game.id,
  gameId: game.gameId,
  queueId: game.queueId,
  championId: game.championId,
  win: game.win,
  kills: game.kills,
  deaths: game.deaths,
  assists: game.assists,
  title: null,
  atMs: game.startedAtMs,
  durationMs: game.durationMs,
  pinned: game.kept,
  status: game.status,
  videoPath: game.videoPath,
  thumbnailPath: game.thumbnailPath,
  sizeBytes: game.sizeBytes,
})

export const clipItem = (clip: Clip): LibraryItem => ({
  kind: 'clip',
  id: clip.id,
  gameId: clip.gameId,
  queueId: clip.queueId,
  championId: clip.championId,
  win: clip.win,
  kills: clip.kills,
  deaths: clip.deaths,
  assists: clip.assists,
  title: clip.title,
  atMs: clip.gameStartedAtMs,
  durationMs: clip.durationMs,
  pinned: clip.favorite,
  status: 'ready',
  videoPath: clip.videoPath,
  thumbnailPath: clip.thumbnailPath,
  sizeBytes: clip.sizeBytes,
})

/** Why the page has nothing to list (`RecordingsEmpty`). */
export type RecordingsEmptyReason = 'unavailable' | 'permission' | 'unsupported' | 'missing' | 'off' | 'nothing' | 'no-match'

export type LibraryKind = 'all' | 'clips' | 'games'
export type LibraryResult = 'all' | 'win' | 'loss'
export type LibraryGrouping = 'day' | 'game' | 'champion' | 'none'

export interface LibraryFilters {
  search: string
  kind: LibraryKind
  championId: number | null
  pinnedOnly: boolean
  /** Empty = every queue. */
  queues: number[]
  result: LibraryResult
  /** Only what was recorded of one game — the way in from a dashboard row whose full game is gone. */
  gameId: number | null
}

export const EMPTY_FILTERS: LibraryFilters = {
  search: '',
  kind: 'all',
  championId: null,
  pinnedOnly: false,
  queues: [],
  result: 'all',
  gameId: null,
}

/** Every item, newest first, before any filter. */
export function libraryItems(library: RecordingLibrary | null): LibraryItem[] {
  if (!library) return []
  return [...library.games.map(gameItem), ...library.clips.map(clipItem)]
    .sort((a, b) => b.atMs - a.atMs || (a.kind === b.kind ? 0 : a.kind === 'game' ? -1 : 1))
}

/** What a card is called: a clip's title, else the game — "Ahri · Victory". */
export function itemTitle(item: LibraryItem, championName: (id: number) => string): string {
  if (item.title) return item.title
  if (item.status === 'recording') return 'Recording…'
  // The champion and the result come with the match history; until then the game has no name of its own.
  const champion = item.championId ? championName(item.championId) : 'Your last game'
  if (item.win === null) return champion
  return `${champion} · ${item.win ? 'Victory' : 'Defeat'}`
}

export function filterItems(items: LibraryItem[], filters: LibraryFilters, championName: (id: number) => string): LibraryItem[] {
  const needle = filters.search.trim().toLowerCase()
  return items.filter((item) => {
    if (filters.kind === 'clips' && item.kind !== 'clip') return false
    if (filters.kind === 'games' && item.kind !== 'game') return false
    if (filters.championId !== null && item.championId !== filters.championId) return false
    if (filters.pinnedOnly && !item.pinned) return false
    if (filters.queues.length && !filters.queues.includes(item.queueId)) return false
    if (filters.result === 'win' && item.win !== true) return false
    if (filters.result === 'loss' && item.win !== false) return false
    if (filters.gameId !== null && item.gameId !== filters.gameId) return false
    if (needle) {
      const champion = item.championId ? championName(item.championId).toLowerCase() : ''
      if (!itemTitle(item, championName).toLowerCase().includes(needle) && !champion.includes(needle)) return false
    }
    return true
  })
}

export interface LibraryGroup {
  key: string
  label: string
  items: LibraryItem[]
}

const dayFormatter = new Intl.DateTimeFormat('en-GB', { weekday: 'long', day: 'numeric', month: 'short' })
const yearFormatter = new Intl.DateTimeFormat('en-GB', { day: 'numeric', month: 'short', year: 'numeric' })

function localDay(ms: number): string {
  const date = new Date(ms)
  return `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`
}

/** "Today", "Yesterday", "Monday 28 Sept", "3 Mar 2025". */
export function dayLabel(ms: number, now = Date.now()): string {
  if (localDay(ms) === localDay(now)) return 'Today'
  if (localDay(ms) === localDay(now - 86_400_000)) return 'Yesterday'
  return new Date(ms).getFullYear() === new Date(now).getFullYear()
    ? dayFormatter.format(ms)
    : yearFormatter.format(ms)
}

/** Cut the (already ordered) list into groups; `none` is one group without a heading. */
export function groupItems(
  items: LibraryItem[],
  grouping: LibraryGrouping,
  championName: (id: number) => string,
): LibraryGroup[] {
  if (grouping === 'none') return items.length ? [{ key: 'all', label: '', items }] : []
  const groups = new Map<string, LibraryGroup>()
  for (const item of items) {
    let key: string
    let label: string
    if (grouping === 'day') {
      key = localDay(item.atMs)
      label = dayLabel(item.atMs)
    }
    else if (grouping === 'game') {
      key = String(item.gameId)
      // The time tells apart two games of the same champion on the same day.
      const time = new Date(item.atMs).toLocaleTimeString('en-GB', { hour: '2-digit', minute: '2-digit' })
      label = `${itemTitle({ ...item, title: null }, championName)} · ${dayLabel(item.atMs)} ${time}`
    }
    else {
      key = String(item.championId ?? 'unknown')
      label = item.championId ? championName(item.championId) : 'Champion not known yet'
    }
    const group = groups.get(key) ?? { key, label, items: [] }
    group.items.push(item)
    groups.set(key, group)
  }
  return [...groups.values()]
}

/** "8 clips", "1 full game", "3 clips · 1 full game". */
export function countLabel(items: LibraryItem[]): string {
  const clips = items.filter(item => item.kind === 'clip').length
  const games = items.length - clips
  const parts = []
  if (clips) parts.push(`${clips} clip${clips === 1 ? '' : 's'}`)
  if (games) parts.push(`${games} full game${games === 1 ? '' : 's'}`)
  return parts.join(' · ') || 'Nothing'
}

/** "2.5 GB", "740 MB". */
export function formatBytes(bytes: number): string {
  const gb = bytes / 1_000_000_000
  if (gb >= 1) return `${gb.toFixed(1)} GB`
  return `${Math.max(0, Math.round(bytes / 1_000_000))} MB`
}

/** "12 / 3 / 16" and "9.3 KDA" (or "Perfect"), when the game's line is known. */
export function recordingKda(item: { kills: number | null, deaths: number | null, assists: number | null }) {
  if (item.kills === null || item.deaths === null || item.assists === null) return null
  const ratio = item.deaths === 0 ? 'Perfect' : `${((item.kills + item.assists) / item.deaths).toFixed(1)} KDA`
  return { line: `${item.kills} / ${item.deaths} / ${item.assists}`, ratio }
}

export const RESOLUTION_LABELS: Record<RecordingResolution, string> = {
  'native': 'Native',
  '1440p': '1440p',
  '1080p': '1080p',
  '720p': '720p',
}

/** "Show in Finder" on a Mac, "Show in Explorer" elsewhere. */
export function revealLabel(): string {
  const platform = typeof navigator === 'undefined' ? '' : navigator.userAgent
  return /Mac/i.test(platform) ? 'Show in Finder' : 'Show in Explorer'
}
