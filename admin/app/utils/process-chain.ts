// Presentation helpers shared by the Processes page's chain, iteration and run
// components (split out of `pages/processes.vue`, #1436). One copy so the lane
// tree, the iteration list and the slide-overs cannot drift apart again.
import type { ProcessRun, ProcessRunStatus } from '~~/shared/types/ops'
import type { ChainLink, ChainOutcome } from '~~/shared/utils/pipeline-lanes'
import { PROCESS_META } from '~~/shared/types/pipeline-chain'
import { processStatusIcon } from '~~/shared/utils/pipeline-health'

// A chain outcome is a run status plus one case the run table has no word for. `notRun`
// and the cockpit's `Missing` are the same claim — there is no run to report — so the
// adapter maps one onto the other rather than restating the whole table. That keeps
// `Skipped` and `notRun` visually distinct for free: both neutral, different icons.
function toHealthStatus(outcome: ChainOutcome): string {
  return outcome === 'notRun' ? 'Missing' : outcome
}
export function outcomeIcon(outcome: ChainOutcome): string {
  return processStatusIcon(toHealthStatus(outcome))
}
export function outcomeLabel(outcome: ChainOutcome): string {
  return outcome === 'notRun' ? 'Not run' : outcome
}

// The icon tint for an outcome, in one place: the same class map used to be
// written out per chip, per iteration chip and per detail row, which is how the
// three drifted before. `Skipped` deliberately inherits the surrounding colour —
// it is neutral, and only its icon distinguishes it from `notRun`.
export function outcomeTextClass(outcome: ChainOutcome): string {
  switch (outcome) {
    case 'Running':
      return 'text-primary animate-spin'
    case 'Success':
      return 'text-success'
    case 'Failed':
      return 'text-error'
    case 'Abandoned':
      return 'text-warning'
    case 'notRun':
      return 'text-dimmed'
    default:
      return ''
  }
}

// The lane's rail — the vertical stroke that makes the branch read as one level
// below the chain — carries the lane's own status, so a failed lane is visible
// before reading a single chip.
export function laneRailClass(outcome: ChainOutcome): string {
  switch (outcome) {
    case 'Running':
      return 'border-primary/50'
    case 'Failed':
      return 'border-error/40'
    case 'Abandoned':
      return 'border-warning/40'
    case 'Success':
      return 'border-success/30'
    default:
      return 'border-default'
  }
}

// Names and explanations both come from the shared PROCESS_META, next to the
// chain order it describes. A process with no entry still renders — under its raw
// class name, with no tooltip — rather than blanking the chip.
export function chainLabel(processName: string): string {
  return PROCESS_META[processName]?.label ?? processName
}

// Shared by every process chip's UTooltip so the usages cannot drift apart from
// each other (they did, silently, for #1314's PIPELINE_CHAIN/PROCESS_META split —
// a literal repeated three times is the same risk).
export const PROCESS_TOOLTIP_UI = { content: 'h-auto max-w-64 items-start px-2.5 py-2' } as const

// The UTooltip on every chip renders the description above a smaller, muted
// context line — what clicking does, or the raw identifier an operator greps
// logs for. Current chain: the context is what clicking does, or — for a step
// that has not run — the outcome, which is the only other thing there is to say.
export function chainTooltipContext(link: ChainLink): string {
  return link.run ? 'View run details' : outcomeLabel(link.outcome)
}
// Iteration chips are not individually clickable, so their context is the raw
// process name (the identifier an operator greps for) and the outcome.
export function iterationChipContext(link: ChainLink): string {
  return `${link.processName} · ${outcomeLabel(link.outcome)}`
}

// The badge passes its icon through the `leadingIcon` slot; spin it only while
// `Running` so the loader-circle actually animates (other statuses stay static).
export function statusBadgeUi(s: ProcessRunStatus): { leadingIcon: string } | undefined {
  return s === 'Running' ? { leadingIcon: 'animate-spin' } : undefined
}

// Process summaries are free-form JSON (flat scalar maps, nested objects, or
// arrays of per-platform rows). `ProcessSummaryView` renders any of those shapes
// as readable fields/tables; the raw JSON stays available as a collapsible
// fallback, which is what this produces.
export function summaryJson(summary: ProcessRun['summary']): string | null {
  if (summary === null || summary === undefined) {
    return null
  }
  return JSON.stringify(summary, null, 2)
}
