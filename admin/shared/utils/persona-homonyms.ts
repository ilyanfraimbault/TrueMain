// Known players sharing a name (#2271): the toasts of the automatic merge (which
// also merges the ones sharing a social channel, #2286) and of the homonyms queue's
// two actions, and what a group's selection sends.
import type { PersonaAutoMerged, PersonaHomonymGroup, PersonaHomonymMarked, PersonaHomonymMerged } from '~~/shared/types/ops'

const plural = (count: number, word: string) => `${count} ${word}${count === 1 ? '' : 's'}`

export function autoMergedMessage(result: PersonaAutoMerged): { title: string, description?: string } {
  const byChannel = result.mergedByChannel ?? 0
  const left = result.skipped > 0 ? `${plural(result.skipped, 'conflict')} left open: changed meanwhile, or marked as different people.` : null
  if (result.merged === 0 && byChannel === 0) {
    return { title: 'Nothing same-name to merge', description: left ?? undefined }
  }

  const merged = [
    result.merged > 0 ? `${plural(result.merged, 'same-name conflict')} merged` : null,
    byChannel > 0 ? `${plural(byChannel, 'same-name player')} sharing a channel merged` : null,
  ].filter(Boolean)
  const details = [
    result.alsoResolved > 0 ? `${plural(result.alsoResolved, 'other conflict')} settled with them.` : null,
    left,
  ].filter(Boolean)
  return {
    title: merged.join(', '),
    description: details.length > 0 ? details.join(' ') : undefined,
  }
}

export function mergedMessage(result: PersonaHomonymMerged): { title: string, description?: string } {
  const pages = result.mergedSlugs.map(slug => `/players/${slug}`).join(', ')
  const also = result.alsoResolved > 0 ? ` ${plural(result.alsoResolved, 'open conflict')} settled with it.` : ''
  return {
    title: `${result.mergedSlugs.join(', ')} merged into ${result.keptSlug}`,
    description: `${pages} now ${result.mergedSlugs.length === 1 ? 'redirects' : 'redirect'}.${also}`,
  }
}

export function markedMessage(result: PersonaHomonymMarked): { title: string } {
  return {
    title: result.marked > 0 ? 'Marked as different people' : 'Already marked as different people',
  }
}

/** Whether two known players of a group are already marked as different people. */
export function markedDistinct(group: Pick<PersonaHomonymGroup, 'distinctPairs'>, a: string, b: string): boolean {
  return group.distinctPairs.some(([x, y]) => (x === a && y === b) || (x === b && y === a))
}

/** A link's key, as the API keys `sharedLinks`: lowercased, without the scheme, a `www.` or a trailing slash. */
export function linkKey(url: string): string {
  return url.toLowerCase().replace(/^https?:\/\/(www\.)?|\/+$/g, '')
}

/** What tells a channel apart at a glance: its handle, the URL's last path segment, decoded. */
export function linkHandle(url: string, max = 18): string {
  const path = url.replace(/^https?:\/\/[^/]+/, '').replace(/[?#].*$/, '').replace(/\/+$/, '')
  const last = path.split('/').pop() ?? ''
  let handle = last
  try {
    handle = decodeURIComponent(last)
  }
  catch {
    // A malformed escape: the raw segment still tells the channel apart.
  }
  if (!handle) {
    return url.replace(/^https?:\/\/(www\.)?/, '')
  }
  return handle.length > max ? `${handle.slice(0, max - 2)}…` : handle
}
