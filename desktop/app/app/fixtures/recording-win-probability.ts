import type { WinProbabilityTimeline } from '#shared/types/win-probability'
import type { RecordingWinProbability } from '~/types/recordings'
import fixture from '#shared/fixtures/win-probability-timeline.json'

/**
 * The recap's win-probability data for `npm run dev` in a browser (dev only,
 * imported from `utils/recordings-dev.ts`): the shared model's own fixture
 * game (`web/shared/fixtures/win-probability-timeline.json`, a blue-side win),
 * with an invented roster and the video starting 20 s before the game clock.
 * The recordings that are not finalised from the timeline get none, as in the
 * shell.
 */

/** The fixture's ten participants, blue side first: Garen, Lee Sin, Ahri, Jinx, Thresh — Darius, Vi, Zed, Caitlyn, Lulu. */
const ROSTER = [86, 64, 103, 222, 412, 122, 254, 238, 51, 117]

/** The player's side in each dev recording that has a curve: the Ahri wins on blue, the Zed loss on red. */
const SIDES: Record<number, number> = { 7212000020: 100, 7212000017: 100, 7212000016: 200 }

export function devWinProbability(gameId: number, durationMs: number | null): RecordingWinProbability | null {
  const teamId = SIDES[gameId]
  if (!teamId) return null
  return {
    teamId,
    champions: ROSTER.map((championId, index) => ({ participantId: index + 1, championId })),
    timeline: fixture.timeline as WinProbabilityTimeline,
    clock: [{ fromGameMs: 0, offsetMs: 20_000 }],
    durationMs,
  }
}
