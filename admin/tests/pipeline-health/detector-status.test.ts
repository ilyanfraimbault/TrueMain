import { describe, expect, it } from 'vitest'
import {
  DETECTOR_STATUS_META,
  detectorStatusMeta,
  detectorValueClass,
} from '~~/shared/utils/detector-status'

// This table is now shared by the data-quality detector lines and the health cockpit's
// tiles (#1031). The property worth pinning is the fallback: an unrecognised status has to
// land on "not measured", because the alternative — falling through to green — would let a
// backend that adds a status make the dashboard claim a pass it never measured.

describe('detectorStatusMeta', () => {
  it.each(['green', 'amber', 'red', 'unknown'] as const)('maps %s to its own meta', (status) => {
    expect(detectorStatusMeta(status)).toBe(DETECTOR_STATUS_META[status])
  })

  it.each([null, undefined, '', 'chartreuse', 'Green'])(
    'falls back to unknown for %s rather than to a pass',
    (status) => {
      expect(detectorStatusMeta(status as string)).toBe(DETECTOR_STATUS_META.unknown)
    },
  )

  it('gives unknown a literal neutral dot, not a surface token', () => {
    // `bg-muted` is a surface colour: an 8px dot painted in it is invisible against the
    // card, which would leave an unmeasured check looking like one with no verdict at all.
    expect(DETECTOR_STATUS_META.unknown.dot).not.toContain('bg-muted')
    expect(DETECTOR_STATUS_META.unknown.color).toBe('neutral')
  })

  it('never labels unknown as passing or failing', () => {
    expect(DETECTOR_STATUS_META.unknown.label).toBe('Not measured')
  })
})

describe('detectorValueClass', () => {
  it('leaves a healthy reading neutral so expanding a list does not repaint the page', () => {
    expect(detectorValueClass('green')).toBe('text-muted')
  })

  it.each([
    ['amber', DETECTOR_STATUS_META.amber.text],
    ['red', DETECTOR_STATUS_META.red.text],
  ])('colours %s to mean "this reading is off"', (status, expected) => {
    expect(detectorValueClass(status)).toBe(expected)
  })

  it('treats an unrecognised status as unmeasured', () => {
    expect(detectorValueClass('nonsense')).toBe(DETECTOR_STATUS_META.unknown.text)
  })
})
