import { describe, expect, it } from 'vitest'
import { channelKey, compareAccounts, compareLinks, compareSummary, compareVerdict } from '~~/shared/utils/persona-compare'
import { persona } from './fixtures'

const link = (kind: string, url: string, source = 'Dpm') => ({ kind, url, source })

describe('persona-compare', () => {
  it('keys a channel across escaping, case, www, query and trailing slash', () => {
    expect(channelKey('https://www.deeplol.gg/strm/%EC%84%B1%ED%9B%88/')).toBe('deeplol.gg/strm/성훈')
    expect(channelKey('http://DeepLol.gg/strm/성훈?ref=lp')).toBe('deeplol.gg/strm/성훈')
    expect(channelKey('https://x.com/%E0%A4%A')).toBe('x.com/%e0%a4%a')
  })

  it('pairs links by kind: same channel, different channel, one side only', () => {
    const left = persona({ links: [link('twitch', 'https://www.twitch.tv/kagee'), link('x', 'https://x.com/MarkusRosn2'), link('lolpros', 'https://lolpros.gg/player/rosen')] })
    const right = persona({ links: [link('x', 'https://x.com/markusrosn2'), link('twitch', 'https://twitch.tv/kage_alt'), link('youtube', 'https://youtube.com/@rosen')] })

    const rows = compareLinks(left, right)

    expect(rows.map(r => [r.kind, r.match])).toEqual([['x', 'same'], ['twitch', 'differs'], ['lolpros', 'only'], ['youtube', 'only']])
    expect(rows[2]!.right).toBeNull()
    expect(rows[3]!.left).toBeNull()
    const summary = compareSummary([], rows)
    expect(summary).toEqual({ sameLinks: 1, differingKinds: ['twitch'], sameAccounts: 0 })
    expect(compareVerdict(summary, rows)).toEqual({ text: '1 of 4 links identical · different twitch', tone: 'success' })
    const differing = compareLinks(persona({ links: [link('x', 'https://x.com/a')] }), persona({ links: [link('x', 'https://x.com/b')] }))
    expect(compareVerdict(compareSummary([], differing), differing)).toEqual({ text: '0 of 1 link identical · different x', tone: 'warning' })
  })

  it('puts the disputed account first and matches accounts by Riot ID', () => {
    const owner = persona()
    const claimant = persona({ accounts: [{ gameName: 'PENG OLD', tagLine: 'euw', platformId: 'EUW1', source: 'Dpm', riotAccountId: null, disputed: false }] })

    const rows = compareAccounts(claimant, owner)

    expect(rows.map(r => [r.disputed, r.match])).toEqual([[true, 'only'], [false, 'same']])
    expect(rows[0]!.left).toBeNull()
  })

  it('reads all-identical links as a match', () => {
    const left = persona({ links: [link('twitch', 'https://twitch.tv/a')] })
    const right = persona({ links: [link('twitch', 'https://www.twitch.tv/A/')] })
    const rows = compareLinks(left, right)
    expect(compareVerdict(compareSummary([], rows), rows)).toEqual({ text: '1 of 1 link identical', tone: 'success' })
    const none = compareLinks(persona({ links: [link('x', 'https://x.com/a')] }), persona({ links: [] }))
    expect(compareVerdict(compareSummary([], none), none).tone).toBe('neutral')
  })
})
