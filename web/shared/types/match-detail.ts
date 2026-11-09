import type { MatchDetailReadModel } from '../contract/api'
import type { WinProbabilityTimeline } from './win-probability'

/**
 * A match's detail as the match panel draws it: the API's read model, plus — in the desktop
 * app only — the client's own timeline, reduced to what the win-probability curve
 * (`buildWinProbability`, #1911) and the gold / XP / CS curves (`buildTimelineCurves`, #2102)
 * read. TrueMain stores no curve (#2009), so the site's match detail has no Timeline tab.
 */
export type MatchDetail = MatchDetailReadModel & {
  winProbabilityTimeline?: WinProbabilityTimeline | null
}
