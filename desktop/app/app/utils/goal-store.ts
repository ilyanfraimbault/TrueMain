import type { Goal } from '~/utils/goals'
import { metricOf } from '~/utils/goals'

/**
 * The player's goals, kept on this machine the way the LP history is
 * (`utils/lp-history.ts`): one store per Riot ID, so a relog never shows another
 * account's goals; a parse that fails reads as no goals, and a private or full
 * store leaves the stored goals as they were. Nothing here is sent anywhere.
 */

/** Finished goals kept for the "Past goals" list; every active one is kept. */
const KEEP_FINISHED = 20

const storageKey = (riotId: string) => `truemain:goals:v1:${riotId}`

const isGoal = (value: unknown, riotId: string): value is Goal => {
  const goal = value as Goal
  return Boolean(goal)
    && typeof goal.id === 'string'
    && goal.riotId === riotId
    && Boolean(metricOf(goal.metric))
    && typeof goal.threshold === 'number'
    && typeof goal.games === 'number'
    && typeof goal.createdAt === 'number'
    && Array.isArray(goal.results)
    && typeof goal.scope?.queue === 'string'
}

export function readGoals(riotId: string): Goal[] {
  try {
    const raw = localStorage.getItem(storageKey(riotId))
    const parsed = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter(entry => isGoal(entry, riotId)) : []
  }
  catch {
    return []
  }
}

/** When a finished goal ended: its last counted game, or its creation if none was. */
const endedAt = (goal: Goal) => goal.results.at(-1)?.playedAt ?? goal.createdAt

/** Every active goal and the latest finished ones, in creation order. */
export function trimGoals(goals: Goal[]): Goal[] {
  const finished = goals
    .filter(goal => goal.status !== 'active')
    .sort((a, b) => endedAt(b) - endedAt(a))
    .slice(0, KEEP_FINISHED)
  const kept = new Set([...goals.filter(goal => goal.status === 'active'), ...finished])
  return goals.filter(goal => kept.has(goal)).sort((a, b) => a.createdAt - b.createdAt)
}

/** Store the account's goals, trimmed, and return what was kept. */
export function saveGoals(riotId: string, goals: Goal[]): Goal[] {
  const kept = trimGoals(goals.filter(goal => goal.riotId === riotId))
  try {
    localStorage.setItem(storageKey(riotId), JSON.stringify(kept))
  }
  catch {
    // A private or full store: the goals hold for this session only.
  }
  return kept
}
