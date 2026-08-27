import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  formatDateTime,
  formatDuration,
  formatNumber,
  formatTimeAgo,
  humanizeBytes,
} from '~~/shared/utils/format'

// The formatters every admin panel renders through. Two rules run across all of them and
// are what these tests are really for: an absent measurement renders as an em dash and
// never as `0`, and no input — negative, non-finite, unparseable — is allowed to reach a
// tile as `NaN`.

describe('humanizeBytes', () => {
  it('renders an empty stat as "0 B" rather than as NaN', () => {
    // A table that has never been measured reports 0 bytes, and every non-finite or
    // negative reading is treated the same way: `Math.log` of any of them is not a size.
    expect(humanizeBytes(0)).toBe('0 B')
    expect(humanizeBytes(-1)).toBe('0 B')
    expect(humanizeBytes(-(1024 ** 3))).toBe('0 B')
    expect(humanizeBytes(Number.NaN)).toBe('0 B')
    expect(humanizeBytes(Number.POSITIVE_INFINITY)).toBe('0 B')
  })

  it('keeps whole bytes whole and gives larger units one decimal', () => {
    expect(humanizeBytes(1)).toBe('1 B')
    expect(humanizeBytes(1023)).toBe('1023 B')
    expect(humanizeBytes(1024)).toBe('1.0 KB')
    expect(humanizeBytes(1536)).toBe('1.5 KB')
  })

  it('steps up a unit every 1024, keeping the conventional labels', () => {
    expect(humanizeBytes(1024 ** 2)).toBe('1.0 MB')
    expect(humanizeBytes(1024 ** 3)).toBe('1.0 GB')
    expect(humanizeBytes(1024 ** 4)).toBe('1.0 TB')
    expect(humanizeBytes(1024 ** 5)).toBe('1.0 PB')
  })

  it('stops at PB instead of running off the end of the unit table', () => {
    // Beyond the largest label the exponent clamps, so the number grows rather than the
    // unit becoming `undefined`.
    expect(humanizeBytes(1024 ** 6)).toBe('1024.0 PB')
  })

  it('honours the requested precision', () => {
    expect(humanizeBytes(1536, 2)).toBe('1.50 KB')
    expect(humanizeBytes(1536, 0)).toBe('2 KB')
  })
})

describe('formatNumber', () => {
  it('groups with the pinned en-US locale so server and client render alike', () => {
    // Hydration: a locale read from the host would differ between the Nitro render and
    // the browser and mismatch the DOM.
    expect(formatNumber(1234567)).toBe('1,234,567')
    expect(formatNumber(999)).toBe('999')
  })

  it('renders a measured zero as zero', () => {
    expect(formatNumber(0)).toBe('0')
  })

  it('renders an absent metric as an em dash rather than as zero', () => {
    expect(formatNumber(null)).toBe('—')
    expect(formatNumber(undefined)).toBe('—')
    expect(formatNumber(Number.NaN)).toBe('—')
    expect(formatNumber(Number.POSITIVE_INFINITY)).toBe('—')
  })
})

describe('formatDateTime', () => {
  it('renders an absent or unparseable instant as an em dash', () => {
    expect(formatDateTime(null)).toBe('—')
    expect(formatDateTime(undefined)).toBe('—')
    expect(formatDateTime('')).toBe('—')
    expect(formatDateTime('not-a-date')).toBe('—')
  })

  it('renders a real instant with a 24-hour clock', () => {
    // The host time zone is not pinned here, so the assertion is on the shape the panel
    // relies on — a short month, a 4-digit year and a 24-hour time — not on the hour.
    expect(formatDateTime('2026-06-09T14:32:00Z'))
      .toMatch(/^[A-Z][a-z]{2} \d{1,2}, \d{4}, \d{2}:\d{2}$/)
  })
})

describe('formatDuration', () => {
  it('renders an absent or nonsensical duration as an em dash', () => {
    expect(formatDuration(null)).toBe('—')
    expect(formatDuration(undefined)).toBe('—')
    expect(formatDuration(Number.NaN)).toBe('—')
    // A negative duration is not a fast run; it is a broken measurement.
    expect(formatDuration(-1)).toBe('—')
  })

  it('renders sub-second runs in whole milliseconds', () => {
    expect(formatDuration(0)).toBe('0ms')
    expect(formatDuration(1)).toBe('1ms')
    expect(formatDuration(999)).toBe('999ms')
    expect(formatDuration(12.6)).toBe('13ms')
  })

  it('crosses to seconds at one second, with one decimal', () => {
    expect(formatDuration(1000)).toBe('1.0s')
    expect(formatDuration(1500)).toBe('1.5s')
    expect(formatDuration(59_000)).toBe('59.0s')
  })

  it('crosses to minutes at one minute and drops a zero seconds remainder', () => {
    expect(formatDuration(60_000)).toBe('1m')
    expect(formatDuration(90_000)).toBe('1m 30s')
    expect(formatDuration(3_599_000)).toBe('59m 59s')
  })

  it('crosses to hours at one hour and drops a zero minutes remainder', () => {
    expect(formatDuration(3_600_000)).toBe('1h')
    expect(formatDuration(5_400_000)).toBe('1h 30m')
    // Long ingestor runs stay readable rather than becoming a five-digit minute count.
    expect(formatDuration(26 * 3_600_000)).toBe('26h')
  })
})

describe('formatTimeAgo', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  function at(now: string) {
    vi.useFakeTimers()
    vi.setSystemTime(new Date(now))
  }

  it('renders an absent or unparseable timestamp as an em dash', () => {
    // The panels use this as a freshness cue; "just now" for an unreadable date would
    // claim the opposite of what is known.
    expect(formatTimeAgo(null)).toBe('—')
    expect(formatTimeAgo(undefined)).toBe('—')
    expect(formatTimeAgo('')).toBe('—')
    expect(formatTimeAgo('not-a-date')).toBe('—')
    expect(formatTimeAgo('2026-13-45T99:99:99Z')).toBe('—')
  })

  it('collapses anything under a minute to "just now"', () => {
    at('2026-06-09T12:00:00Z')
    expect(formatTimeAgo('2026-06-09T12:00:00Z')).toBe('just now')
    expect(formatTimeAgo('2026-06-09T11:59:30Z')).toBe('just now')
  })

  it('steps through minutes, hours and days', () => {
    at('2026-06-09T12:00:00Z')
    expect(formatTimeAgo('2026-06-09T11:59:00Z')).toBe('1m ago')
    expect(formatTimeAgo('2026-06-09T11:05:00Z')).toBe('55m ago')
    expect(formatTimeAgo('2026-06-09T11:00:00Z')).toBe('1h ago')
    expect(formatTimeAgo('2026-06-08T13:00:00Z')).toBe('23h ago')
    expect(formatTimeAgo('2026-06-08T12:00:00Z')).toBe('1d ago')
    expect(formatTimeAgo('2026-06-04T12:00:00Z')).toBe('5d ago')
  })

  it('does not render a clock-skewed future timestamp as a negative age', () => {
    at('2026-06-09T12:00:00Z')
    expect(formatTimeAgo('2026-06-09T12:05:00Z')).toBe('just now')
  })
})
