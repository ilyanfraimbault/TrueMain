import type {
  CaptureAvailability,
  Clip,
  GameRecording,
  Moment,
  MomentKind,
  RecordingFrameRate,
  RecordingResolution,
  RecordingSettingsView,
  RecordingStatus,
} from '~/types/recordings'

/**
 * A recordings library for `npm run dev` in a browser (dev only, imported from
 * `utils/recordings-dev.ts`). Invented games and clips, dated relative to now
 * so the page always has a "Today" and a "Yesterday"; the full games reuse the
 * `lobby` scenario's game ids so its dashboard rows offer "Watch".
 *
 * `?recordings=off|empty|permission|unsupported|missing` in the URL opens the
 * page on the matching empty or unavailable state instead.
 */

export const DEV_FOLDER = '/Users/player/Movies/TrueMain'
const MINUTE = 60_000
const HOUR = 60 * MINUTE
const DAY = 24 * HOUR

/** The size of an hour at a preset, from one bits-per-pixel constant — a stand-in for the shell's estimate. */
export function devBytesPerHour(resolution: RecordingResolution, frameRate: RecordingFrameRate): number | null {
  const height = { 'native': 0, '1440p': 1440, '1080p': 1080, '720p': 720 }[resolution]
  if (!height) return null
  return Math.round(((height * 16) / 9) * height * frameRate * 0.08 * 3600 / 8)
}

/** A small seeded generator, so the fixtures are the same on every reload. */
function random(seed: number) {
  let state = seed
  return () => {
    state = (state + 0x6D2B79F5) | 0
    let t = Math.imul(state ^ (state >>> 15), 1 | state)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4_294_967_296
  }
}

interface GameSpec {
  id: string
  gameId: number
  queueId: number
  agoMs: number
  durationMs: number
  championId: number | null
  win: boolean | null
  kda: [number, number, number] | null
  /** Kills in a row, one entry per kill moment. */
  runs: number[]
  status: GameRecording['status']
  kept?: boolean
}

function momentsOf(spec: GameSpec): Moment[] {
  const next = random(spec.gameId % 100_000)
  const at = () => Math.round(90_000 + next() * (spec.durationMs - 120_000))
  const moments: Moment[] = []
  const add = (kind: MomentKind, videoMs: number, kills = 0, ally: boolean | null = null, endVideoMs = videoMs) =>
    moments.push({ kind, videoMs, endVideoMs, kills, ally })

  for (const run of spec.runs) {
    const start = at()
    add('kill', start, run, null, start + (run - 1) * 4_500)
  }
  for (let index = 0; index < (spec.kda?.[1] ?? 0); index++) add('death', at())
  for (let index = 0; index < (spec.kda?.[2] ?? 0); index++) add('assist', at())

  const objectives: [MomentKind, number][] = [
    ['grubs', 6.2], ['dragon', 5.6], ['tower', 13.8], ['herald', 15.1], ['dragon', 11.4], ['tower', 17.2],
    ['dragon', 17.9], ['atakhan', 20.3], ['tower', 22.6], ['baron', 25.4], ['inhibitor', 27.9], ['elder', 31.5],
  ]
  for (const [kind, minute] of objectives) {
    const videoMs = Math.round(minute * MINUTE + next() * 40_000)
    if (videoMs < spec.durationMs - 30_000) add(kind, videoMs, 0, next() < (spec.win ? 0.7 : 0.35))
  }
  return moments.sort((a, b) => a.videoMs - b.videoMs)
}

const GAMES: GameSpec[] = [
  { id: 'game-7212000021', gameId: 7212000021, queueId: 420, agoMs: 36 * MINUTE, durationMs: 27 * MINUTE + 41_000, championId: null, win: null, kda: null, runs: [1, 2, 1], status: 'processing' },
  { id: 'game-7212000020', gameId: 7212000020, queueId: 420, agoMs: 2 * HOUR, durationMs: 1_969_000, championId: 103, win: true, kda: [5, 4, 16], runs: [2, 1, 1, 1], status: 'ready' },
  { id: 'game-7212000017', gameId: 7212000017, queueId: 420, agoMs: DAY + 3 * HOUR, durationMs: 1_824_000, championId: 103, win: true, kda: [8, 2, 12], runs: [3, 1, 2, 1, 1], status: 'ready', kept: true },
  { id: 'game-7212000016', gameId: 7212000016, queueId: 420, agoMs: 3 * DAY, durationMs: 2_192_000, championId: 238, win: false, kda: [3, 6, 6], runs: [1, 1, 1], status: 'ready' },
]

/** Games whose full recording was deleted: only their clips are left. */
const GONE: GameSpec[] = [
  { id: 'gone-7212000019', gameId: 7212000019, queueId: 420, agoMs: 3 * HOUR, durationMs: 1_556_000, championId: 103, win: true, kda: [12, 0, 7], runs: [3, 4, 2, 1, 1, 1], status: 'ready' },
  { id: 'gone-7211999990', gameId: 7211999990, queueId: 440, agoMs: 9 * DAY, durationMs: 2_051_000, championId: 134, win: true, kda: [14, 3, 9], runs: [5, 2, 1, 1, 1, 1, 1, 1, 1], status: 'ready' },
]

const BYTES_PER_MS = (devBytesPerHour('1080p', 30) ?? 0) / HOUR

function toRecording(spec: GameSpec, now: number): GameRecording {
  const ready = spec.status !== 'recording'
  return {
    id: spec.id,
    gameId: spec.gameId,
    queueId: spec.queueId,
    startedAtMs: now - spec.agoMs,
    status: spec.status,
    durationMs: ready ? spec.durationMs : null,
    championId: spec.championId,
    win: spec.win,
    kills: spec.kda?.[0] ?? null,
    deaths: spec.kda?.[1] ?? null,
    assists: spec.kda?.[2] ?? null,
    kept: spec.kept ?? false,
    sizeBytes: Math.round(spec.durationMs * BYTES_PER_MS),
    videoPath: `${DEV_FOLDER}/games/${spec.id}/video.mp4`,
    thumbnailPath: ready ? `${DEV_FOLDER}/games/${spec.id}/thumbnail.jpg` : null,
    moments: momentsOf(spec),
    highlightsSource: spec.status === 'processing' ? 'live' : 'timeline',
  }
}

/** A clip of `[startMs, endMs]` of a game, its moments made relative to its start — as `clip_save` cuts one. */
export function cutClip(
  game: Pick<GameRecording, 'gameId' | 'queueId' | 'championId' | 'win' | 'kills' | 'deaths' | 'assists' | 'startedAtMs' | 'moments'>,
  recordingId: string | null,
  startMs: number,
  endMs: number,
  title: string,
  createdAtMs: number,
  favorite = false,
): Clip {
  const id = `clip-${game.gameId}-${Math.round(startMs / 1000)}`
  return {
    id,
    title,
    gameId: game.gameId,
    recordingId,
    queueId: game.queueId,
    championId: game.championId,
    win: game.win,
    kills: game.kills,
    deaths: game.deaths,
    assists: game.assists,
    gameStartedAtMs: game.startedAtMs,
    createdAtMs,
    startMs,
    endMs,
    durationMs: endMs - startMs,
    favorite,
    sizeBytes: Math.round((endMs - startMs) * BYTES_PER_MS),
    videoPath: `${DEV_FOLDER}/clips/${id}.mp4`,
    thumbnailPath: `${DEV_FOLDER}/clips/${id}.jpg`,
    moments: game.moments
      .filter(moment => moment.videoMs >= startMs && moment.videoMs <= endMs)
      .map(moment => ({ ...moment, videoMs: moment.videoMs - startMs, endVideoMs: moment.endVideoMs - startMs })),
  }
}

/** A clip framing the game's kill run of `size` kills (its biggest when none is that size). */
function runClip(game: GameRecording, recordingId: string | null, size: number, title: string, favorite = false): Clip {
  const kills = game.moments.filter(moment => moment.kind === 'kill').sort((a, b) => b.kills - a.kills)
  const moment = kills.find(entry => entry.kills === size) ?? kills[0]!
  return cutClip(game, recordingId, Math.max(0, moment.videoMs - 8_000), moment.endVideoMs + 6_000, title, game.startedAtMs + (game.durationMs ?? 0) + 5 * MINUTE, favorite)
}

export function devLibrary(now = Date.now()) {
  const games = GAMES.map(spec => toRecording(spec, now))
  const [ahriToday, ahriYesterday] = [games[1]!, games[2]!]
  const [ahriGone, syndraGone] = GONE.map(spec => toRecording(spec, now))
  const clips: Clip[] = [
    runClip(ahriToday, ahriToday.id, 2, 'Double kill on Ahri', true),
    runClip(ahriGone!, null, 4, 'Quadra kill on Ahri', true),
    runClip(ahriGone!, null, 3, 'Triple kill on Ahri'),
    runClip(ahriYesterday, ahriYesterday.id, 3, 'Triple kill on Ahri'),
    runClip(ahriYesterday, ahriYesterday.id, 2, 'Double kill'),
    runClip(syndraGone!, null, 5, 'Pentakill on Syndra', true),
  ].sort((a, b) => b.gameStartedAtMs - a.gameStartedAtMs || b.startMs - a.startMs)
  return { games, clips }
}

export function devSettings(): RecordingSettingsView {
  const presets: RecordingResolution[] = ['native', '1440p', '1080p', '720p']
  return {
    settings: {
      enabled: true,
      queues: 'ranked',
      quality: { resolution: '1080p', frameRate: 30 },
      budgetBytes: 20_000_000_000,
      folder: null,
    },
    folder: DEV_FOLDER,
    minBudgetBytes: 5_000_000_000,
    estimates: presets.flatMap(resolution => ([30, 60] as const).map(frameRate => ({
      resolution,
      frameRate,
      bytesPerHour: devBytesPerHour(resolution, frameRate),
    }))),
  }
}

export function devStatus(availability: CaptureAvailability): RecordingStatus {
  const messages: Record<CaptureAvailability, string | null> = {
    ready: null,
    permission: 'Screen Recording is not allowed for TrueMain.',
    unsupported: 'Recording is not available on Windows yet.',
    missing: 'The capture helper is missing from this install.',
  }
  return { availability, message: messages[availability], recordingGameId: null, processingGameId: 7212000021 }
}
