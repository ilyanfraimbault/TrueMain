// Parsing and presentation for the "Add in bulk" form of the Accounts hub's
// "Add mains" tab (split out of `AccountsSeed.vue`, #1436). Pure — the run state
// lives in `useBulkSeed`.
import type { BadgeColor, SeedRequestStatus } from '~~/shared/types/ops'
import type { TrackedRegion } from '~~/shared/utils/regions'
import { parseTrackedRegion } from '~~/shared/utils/regions'
import { riotIdError, splitRiotId } from '~~/shared/utils/riot-id'

// Per-row seeding outcome, kept separate from the parsed-row identity so a
// re-parse (editing the textarea) doesn't wipe an in-flight/finished run until
// the user actually changes the rows.
// `duplicate` = the backend returned an existing (still-unprocessed) request
// instead of creating one, i.e. this account was already seeded. It's a soft
// outcome (no work lost), distinct from a hard `failed`.
export type BulkRowOutcome = 'pending' | 'queued' | 'ok' | 'duplicate' | 'failed'

export interface BulkParsedRow {
  // Stable identity for dedupe + outcome tracking (lowercased triple).
  key: string
  lineNo: number
  gameName: string
  tagLine: string
  region: TrackedRegion
  valid: boolean
  reason: string | null
}

export interface BulkPreviewRow extends BulkParsedRow {
  outcome: BulkRowOutcome
  /** Resulting status from the backend (e.g. Pending) when queued. */
  status: SeedRequestStatus | null
  error: string | null
}

// A line is `gameName#tagLine` with an optional `,REGION` suffix. Region tokens
// are matched case-insensitively against the tracked set; an unknown region is
// a hard error (we don't silently fall back, to avoid seeding the wrong shard).
//
// The Riot ID half is judged by `shared/utils/riot-id`, i.e. by the same rules
// the ops endpoints apply — this parser used to be laxer (no length cap, a
// second '#' swallowed into the tag), so the preview counted lines as valid
// that the API would have refused.
function parseLine(line: string, lineNo: number, fallback: TrackedRegion): BulkParsedRow | null {
  const trimmed = line.trim()
  if (!trimmed) {
    return null // blank lines are ignored, not flagged
  }

  let body = trimmed
  let region: TrackedRegion = fallback
  let regionError: string | null = null

  const commaIdx = trimmed.lastIndexOf(',')
  if (commaIdx !== -1) {
    body = trimmed.slice(0, commaIdx).trim()
    const regionToken = trimmed.slice(commaIdx + 1).trim()
    const match = parseTrackedRegion(regionToken)
    if (match) {
      region = match
    }
    else {
      regionError = `Unknown region "${regionToken}"`
    }
  }

  const { gameName, tagLine } = splitRiotId(body)
  const reason: string | null = regionError ?? riotIdError(body)

  return {
    key: `${gameName.toLowerCase()}#${tagLine.toLowerCase()}@${region}`,
    lineNo,
    gameName,
    tagLine,
    region,
    valid: reason === null,
    reason,
  }
}

// Parsed rows of the textarea. Duplicate valid entries (same name#tag@region)
// collapse to the first occurrence and the rest are flagged so the operator sees
// why a count dropped. Invalid lines are always kept.
export function parseBulkSeedLines(raw: string, fallback: TrackedRegion): BulkParsedRow[] {
  const lines = raw.split('\n')
  const seen = new Set<string>()
  const out: BulkParsedRow[] = []
  lines.forEach((line, idx) => {
    const row = parseLine(line, idx + 1, fallback)
    if (!row) {
      return
    }
    if (row.valid) {
      if (seen.has(row.key)) {
        out.push({ ...row, valid: false, reason: 'Duplicate of an earlier line' })
        return
      }
      seen.add(row.key)
    }
    out.push(row)
  })
  return out
}

export function bulkOutcomeBadge(row: BulkPreviewRow): { color: BadgeColor, icon: string, label: string } {
  if (!row.valid) {
    return { color: 'error', icon: 'i-lucide-circle-x', label: 'Invalid' }
  }
  switch (row.outcome) {
    case 'ok':
      return { color: 'success', icon: 'i-lucide-circle-check', label: 'Queued' }
    case 'duplicate':
      return { color: 'warning', icon: 'i-lucide-circle-alert', label: 'Already seeded' }
    case 'failed':
      return { color: 'error', icon: 'i-lucide-circle-x', label: 'Failed' }
    case 'queued':
      return { color: 'info', icon: 'i-lucide-loader', label: 'Sending…' }
    default:
      return { color: 'neutral', icon: 'i-lucide-circle-dashed', label: 'Ready' }
  }
}
