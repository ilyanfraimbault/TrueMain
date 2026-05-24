// Short display labels for Riot queue ids — covers the queues that
// actually show up in our match feed. Anything outside this table falls
// through to a sensible default rather than a bare `Queue 1234`, so a
// brand-new mode Riot ships before we update this file still gets a
// readable label.
//
// Keys sourced from CommunityDragon's queues.json (more current than the
// public static.developer.riotgames.com/docs/lol/queues.json, which
// lags the live game data).

const QUEUE_LABELS: Record<number, string> = {
  0: 'Custom',

  // Summoner's Rift queues
  400: 'Normal',
  420: 'Solo/Duo',
  430: 'Blind',
  440: 'Flex',
  480: 'Swiftplay',

  // Co-op vs AI
  830: 'Co-op vs AI',
  840: 'Co-op vs AI',
  850: 'Co-op vs AI',
  890: 'Co-op vs AI',

  // ARAM + featured modes
  450: 'ARAM',
  700: 'Clash',
  720: 'Clash',
  900: 'URF',
  1020: 'One for All',
  1300: 'Nexus Blitz',
  1400: 'Ultimate Spellbook',
  1900: 'Pick URF',

  // Arena — Rings of Wrath. 1700/1710 = standard 2v2v2v2 (variants
  // across patches); 1750 = "Arena 3x6" — a wider 3-team x 6-player
  // variant. All are CHERRY game mode under the hood; we collapse the
  // visible label so the user sees a single recognisable "Arena" entry
  // instead of a queue-id detail that means nothing in the context of
  // a match row.
  1700: 'Arena',
  1710: 'Arena',
  1750: 'Arena',

  // Tournament + custom-room queues — rare in the truemain feed but
  // they're real Riot queue ids that surface for some players.
  3100: 'Custom',
  3130: 'Tournament',
  3200: 'Custom',
}

// CommunityDragon's `gameMode` column lets us classify queues we don't
// have a hard-coded label for. Mostly useful as a "we know it's a
// recognisable mode even if the queue id is new" net under unknown ids.
const GAME_MODE_LABELS: Record<string, string> = {
  CLASSIC: 'Normal',
  ARAM: 'ARAM',
  CHERRY: 'Arena',
  URF: 'URF',
  SWIFTPLAY: 'Swiftplay',
}

/**
 * Returns a short display label for a Riot queue id. Falls back to the
 * <c>gameMode</c> string (when provided and known) so a new variant of
 * an existing mode still reads as that mode rather than "Queue 1751".
 * Last resort is a placeholder so the row never renders a blank slot.
 */
export function getQueueLabel(queueId: number, gameMode?: string): string {
  const mapped = QUEUE_LABELS[queueId]
  if (mapped) return mapped

  if (gameMode) {
    const modeLabel = GAME_MODE_LABELS[gameMode.toUpperCase()]
    if (modeLabel) return modeLabel
  }

  return queueId === 0 ? 'Custom' : `Queue ${queueId}`
}
