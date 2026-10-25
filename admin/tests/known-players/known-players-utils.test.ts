import { describe, expect, it } from 'vitest'
import { evidenceBadges } from '~~/shared/utils/persona-evidence'
import {
  accountInput,
  accountLabel,
  accountOutcomeMessage,
  accountsSummary,
  ACCOUNT_PLATFORMS,
  conflictHref,
  displayNameError,
  editErrorMessage,
  exclusionReasonLabel,
  linkUrlError,
  slugError,
  slugify,
} from '~~/shared/utils/known-players'

describe('slugify', () => {
  it('turns a name into the backend\'s lowercase kebab key', () => {
    expect(slugify('Caps')).toBe('caps')
    expect(slugify('  Hide on bush  ')).toBe('hide-on-bush')
    expect(slugify('Rekkles (G2)')).toBe('rekkles-g2')
    expect(slugify('Élégant Joueur')).toBe('elegant-joueur')
    expect(slugify('---')).toBe('')
    expect(slugify('a'.repeat(70))).toHaveLength(64)
    expect(slugify(`${'a'.repeat(63)} b`)).toBe('a'.repeat(63))
  })
})

describe('validation', () => {
  it('accepts what the backend accepts', () => {
    expect(slugError('caps-g2')).toBeNull()
    expect(slugError('')).toBe('A slug is required')
    expect(slugError('Caps')).toContain('Lowercase')
    expect(slugError('a--b')).toContain('Lowercase')
    expect(slugError('a'.repeat(65))).toContain('64')
    expect(displayNameError('Caps')).toBeNull()
    expect(displayNameError('   ')).toBe('A name is required')
    expect(displayNameError('x'.repeat(65))).toContain('64')
    expect(linkUrlError('https://www.twitch.tv/caps')).toBeNull()
    expect(linkUrlError('ftp://x.test')).toBe('An http(s) URL')
    expect(linkUrlError('twitch.tv/caps')).toContain('absolute')
  })

  it('splits a typed Riot ID into the account body', () => {
    expect(accountInput('G2 Caps#1323', 'EUW1')).toEqual({ gameName: 'G2 Caps', tagLine: '1323', platformId: 'EUW1' })
    expect(accountInput('G2 Caps', 'EUW1')).toEqual({ error: 'Missing "#tagLine"' })
    expect(ACCOUNT_PLATFORMS.slice(0, 3)).toEqual(['EUW1', 'KR', 'NA1'])
  })
})

describe('labels', () => {
  it('shows a rename next to the published Riot ID', () => {
    const account = { gameName: 'Caps', tagLine: 'EUW', currentGameName: 'G2 Caps', currentTagLine: '1323' }
    expect(accountLabel(account)).toBe('Caps#EUW → now G2 Caps#1323')
    expect(accountLabel({ ...account, currentGameName: 'caps', currentTagLine: 'euw' })).toBe('Caps#EUW')
    expect(accountLabel({ ...account, currentGameName: null, currentTagLine: null })).toBe('Caps#EUW')
  })

  it('summarises accounts and exclusions', () => {
    expect(accountsSummary({ trackedAccounts: 2, unresolvedAccounts: 1 })).toBe('2 tracked · 1 untracked')
    expect(accountsSummary({ trackedAccounts: 0, unresolvedAccounts: 0 })).toBe('No account')
    expect(exclusionReasonLabel('conflict')).toBe('lost in a conflict')
    expect(exclusionReasonLabel('takedown')).toBe('taken down')
    expect(exclusionReasonLabel('unlinked')).toBe('unlinked by an operator')
    expect(conflictHref('c 1')).toBe('/known-players?status=open&conflict=c%201')
  })

  it('words every intake answer to a manual account', () => {
    for (const status of ['linked', 'seeded', 'pending', 'untracked']) {
      expect(accountOutcomeMessage(status).tone).toBe('success')
    }
    for (const status of ['conflict', 'excluded', 'invalid', 'whatever']) {
      expect(accountOutcomeMessage(status).tone).toBe('warning')
    }
    expect(accountOutcomeMessage('conflict').title).toContain('another known player')
  })

  it('reads the API\'s problem details', () => {
    expect(editErrorMessage({ data: { detail: 'This link is already listed.' } })).toBe('This link is already listed.')
    expect(editErrorMessage({ data: { title: 'Not Found' } })).toBe('Not Found')
    expect(editErrorMessage(new Error('boom'))).toBe('The change was not saved.')
    expect(editErrorMessage(null, 'Nope.')).toBe('Nope.')
  })
})

describe('evidenceBadges', () => {
  it('says what the two players share, social channels first', () => {
    expect(evidenceBadges({ score: 23, sharedSocial: ['twitch', 'x'], sharedReferences: 1, differingSocial: [] })).toEqual([
      { label: 'same twitch, x', color: 'success' },
      { label: '1 shared page', color: 'neutral' },
    ])
    expect(evidenceBadges({ score: 1, sharedSocial: [], sharedReferences: 2, differingSocial: ['youtube'] })).toEqual([
      { label: '2 shared pages', color: 'info' },
      { label: 'different youtube', color: 'warning' },
    ])
    expect(evidenceBadges({ score: 0, sharedSocial: [], sharedReferences: 0, differingSocial: [] })).toEqual([
      { label: 'nothing in common', color: 'neutral' },
    ])
  })
})
