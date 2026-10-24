import type { PersonaAdminDetail, PersonaListRow } from '~~/shared/types/ops'

export function row(over: Partial<PersonaListRow> = {}): PersonaListRow {
  return {
    id: 'p1',
    slug: 'caps',
    displayName: 'Caps',
    source: 'Leaguepedia',
    displayNameLocked: false,
    createdAtUtc: '2026-10-14T00:00:00Z',
    updatedAtUtc: '2026-10-22T00:00:00Z',
    removedAtUtc: null,
    trackedAccounts: 2,
    unresolvedAccounts: 0,
    links: 3,
    openConflicts: 0,
    ...over,
  }
}

export function detail(over: Partial<PersonaAdminDetail> = {}): PersonaAdminDetail {
  return {
    id: 'p1',
    slug: 'caps',
    displayName: 'Caps',
    source: 'Leaguepedia',
    displayNameLocked: false,
    createdAtUtc: '2026-10-14T00:00:00Z',
    updatedAtUtc: '2026-10-22T00:00:00Z',
    removedAtUtc: null,
    mergedIntoSlug: null,
    accounts: [
      {
        id: 'a1',
        gameName: 'G2 Caps',
        tagLine: '1323',
        platformId: 'EUW1',
        source: 'Leaguepedia',
        riotAccountId: 'r1',
        currentGameName: 'G2 Caps',
        currentTagLine: '1323',
        createdAtUtc: '2026-10-14T00:00:00Z',
      },
      {
        id: 'a2',
        gameName: 'Caps Alt',
        tagLine: 'EUW',
        platformId: 'EUW1',
        source: 'Dpm',
        riotAccountId: null,
        currentGameName: null,
        currentTagLine: null,
        createdAtUtc: '2026-10-15T00:00:00Z',
      },
    ],
    links: [
      { id: 'l1', kind: 'twitch', url: 'https://www.twitch.tv/caps', source: 'Leaguepedia', createdAtUtc: '2026-10-14T00:00:00Z' },
    ],
    exclusions: [],
    removedLinks: [],
    conflicts: [],
    ...over,
  }
}
