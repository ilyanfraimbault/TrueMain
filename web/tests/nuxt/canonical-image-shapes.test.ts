import { describe, expect, it } from 'vitest'
import { useCanonicalIcon, useCanonicalLoadingArt, useCanonicalSplash } from '~~/app/composables/useCanonicalIcon'
import { IPX_ROUTE_BASE, IPX_SHAPES, ipxModifiers } from '~~/shared/utils/ipx'

// The warm-up and the disk cache spell a shape's URL with `ipxModifiers`; the
// components with @nuxt/image. A warmed image is only useful if the two agree.
describe('IPX shapes', () => {
  const src = 'https://ddragon.leagueoflegends.com/cdn/16.21.1/img/champion/Ahri.png'

  it.each([
    ['icon', () => useCanonicalIcon()(src), IPX_SHAPES.icon],
    ['splash', () => useCanonicalSplash()(src), IPX_SHAPES.splash],
    ['loading art', () => useCanonicalLoadingArt()(src), IPX_SHAPES.loadingArt],
  ] as const)('writes the %s URL the warm-up asks for', (_kind, build, shape) => {
    expect(build()).toBe(`${IPX_ROUTE_BASE}/${ipxModifiers(shape)}/${src}`)
  })
})
