import type { AuditEvent } from '~~/shared/types/ops'

// How the Audit page and the known-player History draw one operator action (#2288):
// what it acted on, as a link to where that thing lives, and its context as chips.

export interface AuditTarget {
  label: string
  to: string | null
}

export interface AuditChip {
  key: string
  value: string
}

/** The acted-on entity: a known player, a conflict, or a seed request in the Add mains queue. */
export function auditTarget(event: AuditEvent): AuditTarget {
  const id = event.targetId ? encodeURIComponent(event.targetId) : null
  const meta = event.metadata
  switch (event.targetType) {
    case 'Persona':
      return { label: meta.slug ?? 'known player', to: id ? `/known-players?player=${id}` : null }
    case 'PersonaAccountConflict':
      return { label: 'conflict', to: id ? `/known-players?status=resolved&conflict=${id}` : null }
    case 'SeedRequest':
      return {
        label: meta.gameName && meta.tagLine ? `${meta.gameName}#${meta.tagLine}` : 'seed request',
        to: '/accounts?view=seed',
      }
    default:
      return { label: event.targetType ?? '', to: null }
  }
}

/** The keys the target link already shows, so their chip would only repeat it. */
function shownByTarget(event: AuditEvent): string[] {
  if (event.targetType === 'Persona') {
    return ['slug']
  }
  return event.targetType === 'SeedRequest' && event.metadata.gameName && event.metadata.tagLine
    ? ['gameName', 'tagLine']
    : []
}

/** The metadata as key/value chips, a rename's `from`/`to` folded into one `from → to`. */
export function auditChips(event: AuditEvent): AuditChip[] {
  const meta = { ...event.metadata }
  const chips: AuditChip[] = []
  if (meta.from !== undefined && meta.to !== undefined) {
    chips.push({ key: 'name', value: `${meta.from} → ${meta.to}` })
    delete meta.from
    delete meta.to
  }
  const hidden = shownByTarget(event)
  for (const [key, value] of Object.entries(meta)) {
    if (!hidden.includes(key)) {
      chips.push({ key, value })
    }
  }
  return chips
}
