import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref, watch } from 'vue'
import type { DraftState } from '~/types/lcu'
import { draftEloBracket } from '~/utils/draft-elo'

/**
 * The bracket the draft requests carry (#1906): the player's tier and above,
 * read from the rank history this machine keeps, and sent with both the pick
 * and the ban requests.
 */

describe('draftEloBracket', () => {
  it('reads the tier and every tier above it', () => {
    expect(draftEloBracket('GOLD')).toBe('GOLD_PLUS')
    expect(draftEloBracket('emerald')).toBe('EMERALD_PLUS')
    expect(draftEloBracket('IRON')).toBe('IRON_PLUS')
  })

  it('keeps Challenger bare, nothing being above it', () => {
    expect(draftEloBracket('CHALLENGER')).toBe('CHALLENGER')
  })

  it('reads every bracket without a known tier', () => {
    expect(draftEloBracket(null)).toBeNull()
    expect(draftEloBracket(undefined)).toBeNull()
    expect(draftEloBracket('NONE')).toBeNull()
    expect(draftEloBracket('')).toBeNull()
  })
})

const lcu = ref<{ riotId: string | null }>({ riotId: 'Faker#KR1' })
const rankHistory = ref<{ tier: string }[]>([])
const posted = vi.hoisted(() => [] as { url: string, body: Record<string, unknown> }[])

const draft = (fields: Partial<DraftState> = {}): DraftState => ({
  myPosition: 'MIDDLE',
  myChampion: null,
  myChampionLocked: false,
  allyChampions: [],
  enemyChampions: [],
  myTeam: [],
  allyBans: [],
  enemyBans: [],
  secondsLeft: 30,
  timerPhase: 'BAN_PICK',
  myAction: null,
  myNextAction: null,
  turnsUntilMyAction: null,
  ...fields,
})

beforeEach(() => {
  vi.useFakeTimers()
  posted.length = 0
  localStorage.clear()
  lcu.value = { riotId: 'Faker#KR1' }
  rankHistory.value = []
  vi.stubGlobal('ref', ref)
  vi.stubGlobal('computed', computed)
  vi.stubGlobal('watch', watch)
  vi.stubGlobal('onScopeDispose', () => {})
  vi.stubGlobal('insideTauri', () => false)
  vi.stubGlobal('useLcuState', () => ({ state: lcu }))
  vi.stubGlobal('usePlayerRecord', () => ({ rankHistory }))
  vi.stubGlobal('$fetch', async (url: string, options: { body: Record<string, unknown> }) => {
    posted.push({ url, body: options.body })
    return url.endsWith('/bans') ? { candidates: [] } : { enemyLanes: [], candidates: [] }
  })
})

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

function standing(riotId: string, tier: string) {
  localStorage.setItem(`truemain:rank-history:${riotId}`, JSON.stringify([
    { capturedAtUtc: '2026-10-01T00:00:00Z', tier: 'SILVER', division: 'I', leaguePoints: 90 },
    { capturedAtUtc: '2026-10-02T00:00:00Z', tier, division: 'IV', leaguePoints: 0 },
  ]))
}

describe('useDraftEloBracket', () => {
  it('takes the latest standing this machine saw for the logged-in player', async () => {
    standing('Faker#KR1', 'GOLD')
    const { useDraftEloBracket } = await import('~/composables/useDraftEloBracket')

    expect(useDraftEloBracket().value).toBe('GOLD_PLUS')
  })

  it('follows the standing read this session over the one stored', async () => {
    standing('Faker#KR1', 'GOLD')
    const { useDraftEloBracket } = await import('~/composables/useDraftEloBracket')
    const bracket = useDraftEloBracket()

    rankHistory.value = [{ tier: 'PLATINUM' }]
    expect(bracket.value).toBe('PLATINUM_PLUS')
  })

  it('reads every bracket for a player never seen ranked, or with nobody logged in', async () => {
    const { useDraftEloBracket } = await import('~/composables/useDraftEloBracket')
    expect(useDraftEloBracket().value).toBeNull()

    standing('Faker#KR1', 'DIAMOND')
    lcu.value = { riotId: null }
    expect(useDraftEloBracket().value).toBeNull()
  })
})

describe('the draft requests', () => {
  it('send the bracket with the picks, and ask again when it changes', async () => {
    const { useDraftRecommendation } = await import('~/composables/useDraftRecommendation')
    const bracket = ref<string | null>('GOLD_PLUS')
    useDraftRecommendation(ref(draft()), ref({}), ref([103]), bracket)

    await vi.advanceTimersByTimeAsync(200)
    expect(posted.at(-1)).toMatchObject({ url: '/api/champions/draft', body: { eloBracket: 'GOLD_PLUS', candidates: [103] } })
    expect(posted.at(-1)!.body).not.toHaveProperty('patch')

    bracket.value = null
    await vi.advanceTimersByTimeAsync(200)
    expect(posted).toHaveLength(2)
    expect(posted.at(-1)!.body.eloBracket).toBeNull()
  })

  it('send the bracket with the bans', async () => {
    const { useDraftBans } = await import('~/composables/useDraftBans')
    useDraftBans(ref(draft()), ref([103, 7]), ref(true), ref('MASTER_PLUS'))

    await vi.advanceTimersByTimeAsync(200)
    expect(posted.at(-1)).toMatchObject({ url: '/api/champions/draft/bans', body: { eloBracket: 'MASTER_PLUS', position: 'MIDDLE' } })
  })
})
