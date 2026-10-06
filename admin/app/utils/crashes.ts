// Crash-report presentation shared by the Crashes tab of `/logs` (`CrashesPanel.vue`)
// and its detail slide-over (`CrashDetail.vue`), split out in #1436.
import type { BadgeColor, CrashReport, CrashSource } from '~~/shared/types/ops'
import { formatElapsed, humanizeBytes } from '~~/shared/utils/format'

export function crashSourceColor(s: CrashSource): BadgeColor {
  switch (s) {
    case 'UncleanShutdown':
    case 'HostRun':
    case 'AppDomainUnhandled':
      return 'error'
    case 'TaskSchedulerUnobserved':
      return 'warning'
    default:
      return 'neutral'
  }
}

export function crashSourceIcon(s: CrashSource): string {
  switch (s) {
    case 'UncleanShutdown':
      return 'i-lucide-skull'
    case 'HostRun':
      return 'i-lucide-circle-x'
    case 'AppDomainUnhandled':
      return 'i-lucide-octagon-alert'
    case 'TaskSchedulerUnobserved':
      return 'i-lucide-triangle-alert'
    default:
      return 'i-lucide-bug'
  }
}

export function crashSourceLabel(s: CrashSource): string {
  switch (s) {
    case 'UncleanShutdown':
      return 'Unclean shutdown'
    case 'HostRun':
      return 'Host run'
    case 'AppDomainUnhandled':
      return 'Unhandled exception'
    case 'TaskSchedulerUnobserved':
      return 'Unobserved task'
    default:
      return s
  }
}

/** The last segment of a fully-qualified exception type, for a narrow table cell. */
export function shortExceptionType(full: string | null): string | null {
  return full ? (full.split('.').pop() ?? full) : null
}

/** A human-readable, copy-pasteable rendering of the whole report. */
export function crashToText(c: CrashReport): string {
  const lines: string[] = []
  lines.push(`Crash report — ${c.processName} — ${crashSourceLabel(c.source)}`)
  if (c.explanation) {
    lines.push(`Reading:   ${c.explanation}`)
  }
  lines.push(`Time:      ${c.timestampUtc}`)
  if (c.exceptionType) {
    lines.push(`Exception: ${c.exceptionType}`)
  }
  if (c.message) {
    lines.push(`Message:   ${c.message}`)
  }
  lines.push(`Host:      ${c.host ?? '—'}`)
  lines.push(`OS:        ${c.osDescription ?? '—'}`)
  lines.push(`Runtime:   ${c.runtimeVersion ?? '—'}`)
  lines.push(`App:       ${c.appVersion ?? '—'}`)
  lines.push(`Uptime:    ${formatElapsed(c.uptimeSeconds * 1000)}`)
  lines.push(`Memory:    working set ${humanizeBytes(c.workingSetBytes)}, managed heap ${humanizeBytes(c.totalManagedMemoryBytes)}`)
  lines.push(`GC:        gen0 ${c.gen0Collections} / gen1 ${c.gen1Collections} / gen2 ${c.gen2Collections}`)
  if (c.exitCode !== null) {
    lines.push(`Exit code: ${c.exitCode}`)
  }
  if (c.stackTrace) {
    lines.push('', 'Stack trace:', c.stackTrace)
  }
  if (c.innerExceptions.length) {
    lines.push('', 'Inner exceptions:')
    for (const e of c.innerExceptions) {
      lines.push(`  ${e.type}: ${e.message}`)
      if (e.stackTrace) {
        lines.push(e.stackTrace)
      }
    }
  }
  if (c.recentLogTail.length) {
    lines.push('', 'Recent log tail:')
    for (const t of c.recentLogTail) {
      lines.push(`  [${t.timestampUtc}] ${t.level} ${t.category}: ${t.message}`)
    }
  }
  return lines.join('\n')
}
