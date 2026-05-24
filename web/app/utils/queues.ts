// Short display labels for Riot queue ids — covers the queues we expect to
// show on the match history. Unknown ids fall back to a generic
// "Custom" / queue id string so the row never blanks out.

const QUEUE_LABELS: Record<number, string> = {
  400: 'Normal',
  420: 'Solo/Duo',
  430: 'Blind',
  440: 'Flex',
  450: 'ARAM',
  700: 'Clash',
  720: 'Clash',
  830: 'Co-op vs AI',
  840: 'Co-op vs AI',
  850: 'Co-op vs AI',
  900: 'URF',
  1020: 'One for All',
  1300: 'Nexus Blitz',
  1400: 'Ultimate Spellbook',
  1700: 'Arena',
  1900: 'Pick URF',
}

export function getQueueLabel(queueId: number): string {
  return QUEUE_LABELS[queueId] ?? `Queue ${queueId}`
}
