import { beforeEach, describe, expect, it } from 'vitest'
import { isIconRepaint, markIconPainted, resetPaintedIcons, warmIconPicture } from '#common/utils/icon-repaint'

const SRC = '/_ipx/f_webp&s_64x64/item/3078.png'

describe('isIconRepaint', () => {
  beforeEach(resetPaintedIcons)

  it('redraws an icon its tooltip re-creates once the picture has been painted', () => {
    markIconPainted(SRC)

    expect(isIconRepaint(true, SRC)).toBe(true)
  })

  it('lets an icon hovered while still loading load as usual', () => {
    expect(isIconRepaint(true, SRC)).toBe(false)
  })

  it('never applies outside a tooltip re-creation, so hydration renders what the server did', () => {
    markIconPainted(SRC)

    expect(isIconRepaint(false, SRC)).toBe(false)
  })

  it('ignores a missing source', () => {
    markIconPainted(null)
    markIconPainted(undefined)

    expect(isIconRepaint(true, null)).toBe(false)
    expect(isIconRepaint(true, '')).toBe(false)
  })
})

describe('warmIconPicture', () => {
  it('has nothing to warm for an icon without a picture', () => {
    const incomplete = { complete: false, naturalWidth: 0, currentSrc: '' } as HTMLImageElement
    const broken = { complete: true, naturalWidth: 0, currentSrc: SRC } as HTMLImageElement

    expect(warmIconPicture(null)).toBeNull()
    expect(warmIconPicture(incomplete)).toBeNull()
    expect(warmIconPicture(broken)).toBeNull()
  })
})
