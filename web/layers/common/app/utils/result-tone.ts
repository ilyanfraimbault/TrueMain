/**
 * The one source for a game's result colours — blue for a win, red for a loss —
 * shared by the match row (edge mark, wash, result label) and the match detail
 * (team headers, the selected player's chip). These name *which side won*, not a
 * measurement: a stat that is good or bad reads on the `--color-data-*` axis
 * (`rate-tone.ts`) instead. Class strings are written out in full so Tailwind's
 * static scan generates them.
 */
export interface ResultTone {
  /** Long label: the row and the team headers. */
  label: 'Victory' | 'Defeat'
  /** Short label: the selected player's chip. */
  short: 'Win' | 'Loss'
  /** Text colour of the result label. */
  text: string
  /** Fill of the row's edge mark. */
  edge: string
  /** Gradient start of the row's wash (pair with `bg-linear-to-r to-transparent`). */
  wash: string
  /** Tinted chip: fill and text together. */
  chip: string
}

const WIN: ResultTone = {
  label: 'Victory',
  short: 'Win',
  text: 'text-sky-400',
  edge: 'bg-sky-400',
  wash: 'from-sky-500/12',
  chip: 'bg-sky-500/15 text-sky-400',
}

const LOSS: ResultTone = {
  label: 'Defeat',
  short: 'Loss',
  text: 'text-red-400',
  edge: 'bg-red-400',
  wash: 'from-red-500/12',
  chip: 'bg-red-500/15 text-red-400',
}

export function resultTone(win: boolean): ResultTone {
  return win ? WIN : LOSS
}
