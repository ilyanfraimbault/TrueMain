import type { BadgeColor, LogLevel } from '~~/shared/types/ops'

// Severity presentation for log rows, shared by the Logs page and the crash
// report's recent-log-tail view.
export function levelColor(l: LogLevel): BadgeColor {
  switch (l) {
    case 'Critical':
    case 'Error':
      return 'error'
    case 'Warning':
      return 'warning'
    case 'Information':
      return 'success'
    default:
      // Debug / Trace — muted.
      return 'neutral'
  }
}

export function levelIcon(l: LogLevel): string {
  switch (l) {
    case 'Critical':
      return 'i-lucide-octagon-alert'
    case 'Error':
      return 'i-lucide-circle-x'
    case 'Warning':
      return 'i-lucide-triangle-alert'
    case 'Information':
      return 'i-lucide-info'
    default:
      return 'i-lucide-bug'
  }
}
