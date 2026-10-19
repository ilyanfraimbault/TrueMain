import { clearVisitorId } from '~/utils/analytics-visitor-id'

/**
 * The visitor's refusal of audience measurement (#2089) — the opt-out the
 * CNIL's consent exemption requires to be reachable from the privacy policy
 * and to last.
 *
 * It is Umami's own flag: the tracker reads `localStorage['umami.disabled']`
 * before every send and drops the event while it is set, so a refusal made
 * after the tracker has loaded takes effect on the next page view without a
 * reload. The plugin also reads it to skip loading the tracker at all.
 * Refusing also removes the anonymous visitor id (#2149) from the device.
 *
 * Storage that throws (Safari private mode, blocked site data) reads as "not
 * opted out" and makes the toggle a no-op — {@link setAnalyticsOptOut} returns
 * the state that actually holds so the control never shows a refusal that was
 * not recorded.
 */
export const ANALYTICS_OPT_OUT_KEY = 'umami.disabled'

function getStorage(): Storage | null {
  try {
    if (typeof window === 'undefined') return null
    return window.localStorage ?? null
  }
  catch {
    return null
  }
}

export function isAnalyticsOptedOut(storage: Storage | null = getStorage()): boolean {
  try {
    return Boolean(storage?.getItem(ANALYTICS_OPT_OUT_KEY))
  }
  catch {
    return false
  }
}

/** Record or lift the refusal; returns the state that now holds. */
export function setAnalyticsOptOut(optOut: boolean, storage: Storage | null = getStorage()): boolean {
  try {
    if (optOut) {
      storage?.setItem(ANALYTICS_OPT_OUT_KEY, '1')
      clearVisitorId(storage)
    }
    else {
      storage?.removeItem(ANALYTICS_OPT_OUT_KEY)
    }
  }
  catch {
    // Fall through: report what the storage actually holds.
  }
  return isAnalyticsOptedOut(storage)
}
