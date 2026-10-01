import { describe, expect, it } from 'vitest'
import { truemainNameTag, truemainProfilePath } from '~~/shared/utils/truemain-path'
import { truemainChampionPath } from '~~/shared/utils/champion-slug'
import { favoriteNameTag } from '#common/utils/favorites'

describe('truemainNameTag', () => {
  it('joins the name and the tag with a hyphen', () => {
    expect(truemainNameTag('Faker', 'KR1')).toBe('Faker-KR1')
  })

  it('is the bare name when the account has no tag', () => {
    expect(truemainNameTag('Faker', null)).toBe('Faker')
    expect(truemainNameTag('Faker', '  ')).toBe('Faker')
  })

  it('trims both halves', () => {
    expect(truemainNameTag(' Faker ', ' KR1 ')).toBe('Faker-KR1')
  })

  it('is the slug the favorites store keys on', () => {
    expect(favoriteNameTag('Faker', 'KR1')).toBe(truemainNameTag('Faker', 'KR1'))
  })
})

describe('truemainProfilePath', () => {
  it('URL-encodes the slug', () => {
    expect(truemainProfilePath('Hide on bush-KR1')).toBe('/truemains/Hide%20on%20bush-KR1')
    expect(truemainProfilePath('Ça va#?-EUW')).toBe('/truemains/%C3%87a%20va%23%3F-EUW')
  })

  it('is the root of the player-scoped champion pages', () => {
    expect(truemainChampionPath('Hide on bush-KR1', 103, { 103: 'ahri' }))
      .toBe('/truemains/Hide%20on%20bush-KR1/champions/ahri')
  })
})
