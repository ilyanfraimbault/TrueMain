import type { PersonaConflict, PersonaOps } from '~~/shared/types/ops'

export function conflict(over: Partial<PersonaConflict> = {}): PersonaConflict {
  return {
    id: 'c1',
    status: 'Open',
    resolution: null,
    source: 'Dpm',
    firstSeenAtUtc: '2026-10-19T04:00:00Z',
    lastSeenAtUtc: '2026-10-22T04:00:00Z',
    timesSeen: 2,
    resolvedAtUtc: null,
    account: {
      gameName: 'twtv Peng04',
      tagLine: 'NMIXX',
      platformId: 'EUW1',
      riotAccountId: 'a1',
      currentGameName: 'twtv Peng04',
      currentTagLine: 'NMIXX',
    },
    claimant: { id: 'p2', slug: 'peng-dpm', displayName: 'Peng (dpm)', source: 'Dpm', accountCount: 4, linkCount: 1 },
    owner: { id: 'p1', slug: 'peng', displayName: 'Peng', source: 'Leaguepedia', accountCount: 1, linkCount: 3 },
    ...over,
  }
}

export function persona(over: Partial<PersonaOps> = {}): PersonaOps {
  return {
    id: 'p1',
    slug: 'peng',
    displayName: 'Peng',
    source: 'Leaguepedia',
    createdAtUtc: '2026-10-14T00:00:00Z',
    accounts: [
      { gameName: 'twtv Peng04', tagLine: 'NMIXX', platformId: 'EUW1', source: 'Leaguepedia', riotAccountId: 'a1', disputed: true },
      { gameName: 'Peng Old', tagLine: 'EUW', platformId: 'EUW1', source: 'Leaguepedia', riotAccountId: null, disputed: false },
    ],
    links: [{ kind: 'twitch', url: 'https://www.twitch.tv/peng', source: 'Leaguepedia' }],
    ...over,
  }
}
