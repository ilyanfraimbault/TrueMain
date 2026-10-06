// Presentation shared by the Data Quality page and its cards (split out of
// `pages/data-quality.vue`, #1436).
import type { DataQualityIssueType, IssueMeta } from '~~/shared/types/ops'

// Issue-type metadata: label, icon, badge color — drives the filter select and
// the group headers/badges so presentation stays consistent across the panel.
// `IssueMeta` / `BadgeColor` live in shared/types/ops so the page and
// DataQualityGroupTable share one definition.
export const DATA_QUALITY_ISSUE_META: Record<DataQualityIssueType, IssueMeta> = {
  missingTimeline: {
    label: 'Missing timeline',
    icon: 'i-lucide-clock-alert',
    color: 'warning',
    description: 'Timeline not ingested past the staleness window — likely stuck.',
  },
  wrongParticipantCount: {
    label: 'Wrong participant count',
    icon: 'i-lucide-users',
    color: 'error',
    description: 'Participant rows differ from the queue’s expected count.',
  },
  missingTeamPosition: {
    label: 'Missing team position',
    icon: 'i-lucide-map-pin-off',
    color: 'error',
    description: 'A team is missing one of the five Summoner’s Rift lanes.',
  },
  zeroDuration: {
    label: 'Zero duration',
    icon: 'i-lucide-timer-off',
    color: 'warning',
    description: 'Game has no recorded length — usually a remake or ingest glitch.',
  },
  duplicateChampion: {
    label: 'Duplicate champion',
    icon: 'i-lucide-copy',
    color: 'error',
    description: 'The same champion appears twice on one team.',
  },
}

export const DATA_QUALITY_ISSUE_ORDER: DataQualityIssueType[] = [
  'missingTimeline',
  'wrongParticipantCount',
  'missingTeamPosition',
  'zeroDuration',
  'duplicateChampion',
]

const QUEUE_LABELS: Record<number, string> = {
  420: 'Ranked Solo',
  430: 'Normal',
  440: 'Ranked Flex',
  450: 'ARAM',
  700: 'Clash',
}

export function dataQualityQueueLabel(queueId: number): string {
  // Queue id 0 isn't a real queue: Riot returns a stub payload (no queue id,
  // zero duration) for games that aborted before stats were recorded, and the
  // ingest stores it verbatim. Name it instead of echoing a bare "Queue 0".
  if (queueId === 0) {
    return 'Unknown queue'
  }
  return QUEUE_LABELS[queueId] ?? `Queue ${queueId}`
}

export function pluralize(count: number, one: string, many: string): string {
  return count === 1 ? one : many
}
