// The operator-action trail (#2288): `GET /ops/audit-events`, newest first, paged.

export interface AuditEventsFilters {
  [key: string]: string | number | undefined
  action?: string
  targetType?: string
  targetId?: string
  actor?: string
  since?: string
  until?: string
  search?: string
  page?: number
  pageSize?: number
}

export interface AuditEvent {
  id: string
  timestampUtc: string
  /** `operator`, `auto` (a same-name or shared-channel sweep) or `ingestor`. */
  actor: string
  /** The stable action name, e.g. `rename_persona`. */
  action: string
  /** The action in plain words; the name itself for an action the API has no words for. */
  label: string
  targetType: string | null
  targetId: string | null
  metadata: Record<string, string>
}

export interface AuditAction {
  name: string
  label: string
}

export interface AuditEventsResponse {
  entries: AuditEvent[]
  total: number
  page: number
  pageSize: number
  actions: AuditAction[]
  actors: string[]
  targetTypes: string[]
}
