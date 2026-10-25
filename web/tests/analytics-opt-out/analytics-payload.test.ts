import { describe, expect, it } from 'vitest'
import {
  isCampaignParam,
  minimizeAnalyticsPayload,
  referrerDomainOnly,
  stripCampaignParams,
} from '~/utils/analytics-payload'

/**
 * The CNIL grid's data-minimisation measures the Umami tracker does not apply
 * itself (#2092): no campaign ids in the page URL, no referrer path.
 */
describe('isCampaignParam', () => {
  it('matches utm_* and ad click ids, whatever the case or encoding', () => {
    for (const name of ['utm_source', 'UTM_Campaign', 'utm_whatever', 'utm%5Fmedium', 'gclid', 'fbclid', 'li_fat_id', 'msclkid']) {
      expect(isCampaignParam(name), name).toBe(true)
    }
  })

  it('leaves the site\'s own parameters alone', () => {
    for (const name of ['patch', 'elo', 'position', 'vs', 'utm', 'autm_source', 'sort', '%E0%A4%A']) {
      expect(isCampaignParam(name), name).toBe(false)
    }
  })
})

describe('stripCampaignParams', () => {
  it('drops campaign parameters and keeps the others, in order and encoding', () => {
    expect(stripCampaignParams('https://truemain.lol/c?utm_source=x&patch=26.20&q=a%20b&fbclid=1&elo=master'))
      .toBe('https://truemain.lol/c?patch=26.20&q=a%20b&elo=master')
  })

  it('removes the query mark once nothing is left, and keeps the hash', () => {
    expect(stripCampaignParams('https://truemain.lol/c?utm_source=x&utm_medium=y#runes'))
      .toBe('https://truemain.lol/c#runes')
  })

  it('handles relative URLs, empty segments and valueless parameters', () => {
    expect(stripCampaignParams('/c?&utm_term&patch=1&')).toBe('/c?patch=1')
  })

  it('returns a URL without a query unchanged', () => {
    expect(stripCampaignParams('https://truemain.lol/c#x?utm_source=1')).toBe('https://truemain.lol/c#x?utm_source=1')
    expect(stripCampaignParams('/')).toBe('/')
  })
})

describe('referrerDomainOnly', () => {
  it('keeps only the origin of an external referrer', () => {
    expect(referrerDomainOnly('https://www.google.com/search?q=truemain+yasuo', 'truemain.lol')).toBe('https://www.google.com/')
    expect(referrerDomainOnly('http://localhost:3000/a/b', 'truemain.lol')).toBe('http://localhost:3000/')
  })

  it('drops this site\'s own pages, empty and unparseable referrers', () => {
    expect(referrerDomainOnly('https://truemain.lol/champions/1?patch=1', 'truemain.lol')).toBe('')
    expect(referrerDomainOnly('', 'truemain.lol')).toBe('')
    expect(referrerDomainOnly('/relative/path', 'truemain.lol')).toBe('')
    expect(referrerDomainOnly('not a url', 'truemain.lol')).toBe('')
  })
})

describe('minimizeAnalyticsPayload', () => {
  it('rewrites url and referrer and passes every other field through', () => {
    const payload = {
      website: 'site-id',
      title: 'Yasuo',
      url: '/c?utm_campaign=launch&patch=1',
      referrer: 'https://t.co/abc',
      data: { utm_source: 'kept-as-event-data' },
    }
    expect(minimizeAnalyticsPayload(payload, 'truemain.lol')).toEqual({
      website: 'site-id',
      title: 'Yasuo',
      url: '/c?patch=1',
      referrer: 'https://t.co/',
      data: { utm_source: 'kept-as-event-data' },
    })
    expect(payload.url).toBe('/c?utm_campaign=launch&patch=1')
  })

  it('leaves a payload without url or referrer as it is', () => {
    expect(minimizeAnalyticsPayload({ website: 'site-id' }, 'truemain.lol')).toEqual({ website: 'site-id' })
  })
})
