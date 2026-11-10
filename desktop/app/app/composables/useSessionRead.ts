/** A failed read retries on its own, the wait doubling up to a ceiling. */
const RETRY_FIRST_MS = 2_000
const RETRY_MAX_MS = 30_000

interface Retry { timer: ReturnType<typeof setTimeout> | undefined, delay: number }
const retries = new Map<string, Retry>()

/**
 * A reference read the app takes once per session and needs eventually — the
 * tier list, the damage profiles: a failure retries on its own until it lands,
 * since one left untried kept the draft's pick panel empty for a whole
 * champion select (#1783). Shared by every caller under `key`.
 */
export function useSessionRead<T>(key: string, read: () => Promise<T>) {
  const data = useState<T | null>(`${key}-data`, () => null)
  const status = useState<'idle' | 'pending' | 'ready' | 'error'>(`${key}-status`, () => 'idle')
  const retry = retries.get(key) ?? { timer: undefined, delay: RETRY_FIRST_MS }
  retries.set(key, retry)

  async function load() {
    if (status.value === 'pending' || status.value === 'ready') return
    status.value = 'pending'
    try {
      data.value = await read()
      status.value = 'ready'
      clearTimeout(retry.timer)
      retry.timer = undefined
      retry.delay = RETRY_FIRST_MS
    }
    catch {
      status.value = 'error'
      if (retry.timer === undefined) {
        retry.timer = setTimeout(() => {
          retry.timer = undefined
          void load()
        }, retry.delay)
        retry.delay = Math.min(retry.delay * 2, RETRY_MAX_MS)
      }
    }
  }
  void load()

  return { data, status, load }
}
