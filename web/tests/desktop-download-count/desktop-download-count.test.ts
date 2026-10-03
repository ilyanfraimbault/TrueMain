import { describe, expect, it } from 'vitest'
import { isAutomatedClient } from '~~/server/utils/desktop-download-count'

// The download count (#1805) is meant to read "a person got the installer":
// what a browser sends is counted, what a crawler or a link preview sends is not.

describe('isAutomatedClient', () => {
  it.each([
    'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36',
    'Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.6 Safari/605.1.15',
  ])('counts a browser: %s', (userAgent) => {
    expect(isAutomatedClient(userAgent)).toBe(false)
  })

  it.each([
    'Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)',
    'Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)',
    'Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)',
    'curl/8.7.1',
    'Mozilla/5.0 HeadlessChrome/129.0.0.0',
  ])('skips an automated client: %s', (userAgent) => {
    expect(isAutomatedClient(userAgent)).toBe(true)
  })

  it('skips a request without a user agent', () => {
    expect(isAutomatedClient(undefined)).toBe(true)
    expect(isAutomatedClient('')).toBe(true)
  })
})
