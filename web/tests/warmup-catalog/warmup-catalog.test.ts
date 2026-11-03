import { describe, expect, it } from 'vitest'
import { collectIconUrls, imagePaths, isDurableImagePath, patchedStaticReads } from '~~/server/utils/warmup-catalog'

describe('warm-up catalog', () => {
  it('asks for the patch-keyed reads in both forms the app sends', () => {
    expect(patchedStaticReads('16.21.1')).toEqual(expect.arrayContaining([
      '/api/static/items?patch=16.21',
      '/api/static/items?patch=16.21.1',
      '/api/static/rune-tree?patch=16.21',
      '/api/static/summoner-spells?patch=16.21.1',
    ]))
  })

  it('finds an icon however deep the read nests it', () => {
    const runeTree = { styles: [{ iconUrl: 'https://cdn/a.png', slots: [{ perks: [{ iconUrl: 'https://cdn/b.png' }] }] }] }

    expect([...collectIconUrls(runeTree)]).toEqual(['https://cdn/a.png', 'https://cdn/b.png'])
  })

  it('ignores an empty icon and survives a read that failed', () => {
    expect([...collectIconUrls({ iconUrl: '' })]).toEqual([])
    expect([...collectIconUrls(null)]).toEqual([])
  })

  it('builds the URLs the components build, so a warmed image is the one they ask for', () => {
    const paths = imagePaths(
      ['https://ddragon.leagueoflegends.com/cdn/16.21.1/img/item/3078.png'],
      ['https://ddragon.leagueoflegends.com/cdn/16.21.1/img/champion/Ahri.png'],
    )

    expect(paths).toContain('/_ipx/f_webp&s_64x64/https://ddragon.leagueoflegends.com/cdn/16.21.1/img/item/3078.png')
    expect(paths).toContain('/_ipx/w_1280&f_webp/https://ddragon.leagueoflegends.com/cdn/img/champion/splash/Ahri_0.jpg')
    expect(paths).toContain('/_ipx/f_webp&s_64x64/positions/icon-position-top.png')
  })

  it('keeps on disk only the shapes the site asks for', () => {
    expect(isDurableImagePath('/_ipx/f_webp&s_64x64/https://cdn/a.png')).toBe(true)
    expect(isDurableImagePath('/_ipx/w_1280&f_webp/https://cdn/a.jpg')).toBe(true)
    expect(isDurableImagePath('/_ipx/s_24x24/https://cdn/a.svg')).toBe(true)

    expect(isDurableImagePath('/_ipx/f_webp&s_65x65/https://cdn/a.png')).toBe(false)
    expect(isDurableImagePath('/_ipx/w_4000/https://cdn/a.jpg')).toBe(false)
    expect(isDurableImagePath('/other/f_webp&s_64x64/https://cdn/a.png')).toBe(false)
  })
})
