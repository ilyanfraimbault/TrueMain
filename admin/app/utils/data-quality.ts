// Presentation shared by the Data Quality page and its cards (split out of
// `pages/data-quality.vue`, #1436).
import type { DataQualityIssueType } from '#site-shared/contract/api'
import type { BadgeColor } from '~~/shared/utils/badge-color'
import { dataQualityIssueTypeValues } from '#site-shared/contract/api'
import { getQueueLabel } from '#site-shared/utils/queues'

/**
 * How one issue type reads — label, icon and badge colour. Drives the filter select,
 * the group headers and the badges, so the panel names a check the same way everywhere.
 */
export interface IssueMeta {
  label: string
  icon: string
  color: BadgeColor
  description: string
}

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

/** The checks in the order the API declares them. */
export const DATA_QUALITY_ISSUE_ORDER = dataQualityIssueTypeValues


export function dataQualityQueueLabel(queueId: number): string {
  // Queue id 0 isn't a real queue: Riot returns a stub payload (no queue id,
  // zero duration) for games that aborted before stats were recorded, and the
  // ingest stores it verbatim. Name it instead of echoing a bare "Queue 0".
  if (queueId === 0) {
    return 'Unknown queue'
  }
  return getQueueLabel(queueId)
}

export function pluralize(count: number, one: string, many: string): string {
  return count === 1 ? one : many
}
