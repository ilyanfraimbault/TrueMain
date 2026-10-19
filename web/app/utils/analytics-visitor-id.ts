/**
 * The anonymous visitor id the audience measurement carries (#2149), so a
 * visitor who comes back next month is recognised as the same one.
 *
 * Umami derives its session from `payload.id` when one is sent —
 * `uuid(websiteId, id)`, independent of IP, user agent and its monthly salt —
 * and from a hash of those three otherwise, which turns every visitor into a
 * new one each month.
 *
 * Bounds set by the CNIL's consent exemption for audience measurement:
 * - The id is random and tied to nothing — not the IP, not a Riot account.
 * - It lives at most {@link VISITOR_ID_LIFETIME_MONTHS} months from its
 *   creation and a later visit never extends that: the stored creation date is
 *   only ever written together with a new id.
 * - A visitor who opted out has none: the plugin removes it instead of reading
 *   it, and {@link setAnalyticsOptOut} removes it on refusal.
 *
 * Storage that throws or does not keep what was written yields no id at all —
 * Umami then falls back to its own hash. A fresh id per page load would count
 * every page as a new visitor.
 */
export const VISITOR_ID_KEY = 'truemain.visitor'
export const VISITOR_ID_LIFETIME_MONTHS = 13

interface StoredVisitorId {
  id: string
  createdAt: number
}

function getStorage(): Storage | null {
  try {
    if (typeof window === 'undefined') return null
    return window.localStorage ?? null
  }
  catch {
    return null
  }
}

function randomUuid(): string | null {
  const crypto = globalThis.crypto
  if (typeof crypto?.randomUUID === 'function') return crypto.randomUUID()
  // `randomUUID` only exists in secure contexts; plain-HTTP previews still have
  // `getRandomValues`, enough for an RFC 4122 version 4 id.
  if (typeof crypto?.getRandomValues !== 'function') return null
  const bytes = crypto.getRandomValues(new Uint8Array(16))
  bytes[6] = (bytes[6]! & 0x0F) | 0x40
  bytes[8] = (bytes[8]! & 0x3F) | 0x80
  const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('')
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`
}

/** When an id created at `createdAt` stops being used. */
export function visitorIdExpiry(createdAt: number): number {
  const expiry = new Date(createdAt)
  expiry.setUTCMonth(expiry.getUTCMonth() + VISITOR_ID_LIFETIME_MONTHS)
  return expiry.getTime()
}

function parse(raw: string | null): StoredVisitorId | null {
  if (!raw) return null
  try {
    const value = JSON.parse(raw) as Partial<StoredVisitorId> | null
    if (typeof value?.id === 'string' && value.id && typeof value.createdAt === 'number' && Number.isFinite(value.createdAt)) {
      return { id: value.id, createdAt: value.createdAt }
    }
  }
  catch {
    // Unreadable value: replaced below like an expired one.
  }
  return null
}

/**
 * The stored id while it is younger than its lifetime, else a new one stored
 * with `now` as its creation date. Null when nothing can be kept on the device.
 */
export function getOrCreateVisitorId(storage: Storage | null = getStorage(), now: number = Date.now()): string | null {
  if (!storage) return null
  try {
    const stored = parse(storage.getItem(VISITOR_ID_KEY))
    // A creation date in the future (a clock set back) is not trusted to bound
    // the lifetime: the id is replaced.
    if (stored && stored.createdAt <= now && now < visitorIdExpiry(stored.createdAt)) {
      return stored.id
    }

    const id = randomUuid()
    if (!id) return null
    const value = JSON.stringify({ id, createdAt: now } satisfies StoredVisitorId)
    storage.setItem(VISITOR_ID_KEY, value)
    return storage.getItem(VISITOR_ID_KEY) === value ? id : null
  }
  catch {
    return null
  }
}

export function clearVisitorId(storage: Storage | null = getStorage()): void {
  try {
    storage?.removeItem(VISITOR_ID_KEY)
  }
  catch {
    // Nothing stored that could be removed.
  }
}
