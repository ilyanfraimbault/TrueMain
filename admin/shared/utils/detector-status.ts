import type { BadgeColor, DetectorStatus } from '~~/shared/types/ops'

/**
 * The one green/amber/red/unknown vocabulary, shared by the data-quality panel's
 * detector lines (#924/#992) and the health cockpit's signal tiles (#1031).
 *
 * Extracted out of `DataQualityDetectorItem.vue` when the cockpit landed: two panels
 * painting the same four statuses from two private tables is how "amber" ends up
 * meaning something slightly different on each page. Kept under `shared/` so it is
 * importable without the Nuxt runtime, which is also the only way it can be tested —
 * the admin suite has no page-level harness.
 */

/** One dot, one colour, one word — the status is stated once. */
export interface DetectorStatusMeta {
  /** Background class for the verdict dot. */
  dot: string
  /** Text colour class for a reading that carries this verdict. */
  text: string
  /** The word an operator reads. */
  label: string
  /** `UBadge`/`UAlert` colour for the same verdict. */
  color: BadgeColor
  /** Lucide icon for the verdict, for the places that lead with an icon. */
  icon: string
}

/**
 * `unknown` is neutral: not a pass and not an alarm. It says "not measured", and
 * dressing it as either would be the dashboard lying.
 */
export const DETECTOR_STATUS_META: Record<DetectorStatus, DetectorStatusMeta> = {
  green: {
    dot: 'bg-success',
    text: 'text-success',
    label: 'Passing',
    color: 'success',
    icon: 'i-lucide-circle-check',
  },
  amber: {
    dot: 'bg-warning',
    text: 'text-warning',
    label: 'Needs attention',
    color: 'warning',
    icon: 'i-lucide-triangle-alert',
  },
  red: {
    dot: 'bg-error',
    text: 'text-error',
    label: 'Failing',
    color: 'error',
    icon: 'i-lucide-circle-x',
  },
  // A literal neutral rather than a semantic background token: `bg-muted` is a
  // surface colour and an 8px dot painted in it is invisible against the card,
  // which would leave an unmeasured check looking like a check with no verdict.
  unknown: {
    dot: 'bg-neutral-400 dark:bg-neutral-500',
    text: 'text-dimmed',
    label: 'Not measured',
    color: 'neutral',
    icon: 'i-lucide-circle-help',
  },
}

/**
 * Meta for a status, falling back to `unknown` for anything unrecognised — a status
 * the backend adds later must render as "not measured", never as a pass.
 */
export function detectorStatusMeta(status: string | null | undefined): DetectorStatusMeta {
  return DETECTOR_STATUS_META[status as DetectorStatus] ?? DETECTOR_STATUS_META.unknown
}

/**
 * Colour on a value means "this reading is off". A healthy row stays neutral so
 * expanding a list doesn't repaint the page.
 */
export function detectorValueClass(status: string | null | undefined): string {
  return status === 'green' ? 'text-muted' : detectorStatusMeta(status).text
}
