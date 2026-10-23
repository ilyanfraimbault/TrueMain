import { describe, expect, it } from 'vitest'
import {
  accountCount,
  conflictActions,
  personaName,
  renamedTo,
  resolutionColor,
  resolutionLabel,
  resolvedMessage,
  resolveErrorMessage,
  riotId,
} from '~~/shared/utils/persona-conflicts'
import { conflict } from './fixtures'

describe('riotId / renamedTo', () => {
  it('writes the Riot ID the source published', () => {
    expect(riotId(conflict().account)).toBe('twtv Peng04#NMIXX')
  })

  it('shows the current Riot ID only when the account was renamed', () => {
    expect(renamedTo(conflict().account)).toBeNull()
    expect(renamedTo({ ...conflict().account, currentGameName: 'TWTV PENG04', currentTagLine: 'nmixx' })).toBeNull()
    expect(renamedTo({ ...conflict().account, currentGameName: 'Peng', currentTagLine: 'EUW' })).toBe('Peng#EUW')
    expect(renamedTo({ ...conflict().account, riotAccountId: null, currentGameName: null, currentTagLine: null })).toBeNull()
  })
})

describe('personaName / accountCount', () => {
  it('adds the slug only when the other side has the same name', () => {
    expect(personaName({ displayName: 'Peng', slug: 'peng' }, { displayName: 'Peng (dpm)' })).toBe('Peng')
    expect(personaName({ displayName: 'Peng', slug: 'peng' }, { displayName: 'PENG' })).toBe('Peng (peng)')
  })

  it('counts accounts in the singular and the plural', () => {
    expect(accountCount(1)).toBe('1 account')
    expect(accountCount(0)).toBe('0 accounts')
    expect(accountCount(1234)).toBe('1,234 accounts')
  })
})

describe('resolution vocabulary', () => {
  it('labels and colours every outcome, open included', () => {
    expect(resolutionLabel(null)).toBe('Open')
    expect(resolutionColor(null)).toBe('warning')
    expect(resolutionLabel('Moved')).toBe('Moved to the claimant')
    expect(resolutionLabel('Kept')).toBe('Kept by the holder')
    expect(resolutionLabel('Detached')).toBe('Detached from both')
    expect(resolutionLabel('Merged')).toBe('Merged')
    expect(['Moved', 'Kept', 'Detached', 'Merged'].map(r => resolutionColor(r as never)))
      .toEqual(['info', 'success', 'error', 'primary'])
  })
})

describe('conflictActions', () => {
  it('offers move, keep, detach and a merge each way, worded for these two players', () => {
    const actions = conflictActions(conflict())
    expect(actions.map(a => a.body)).toEqual([
      { action: 'move' },
      { action: 'keep' },
      { action: 'detach' },
      { action: 'merge', keep: 'owner' },
      { action: 'merge', keep: 'claimant' },
    ])
    expect(actions[0]!.label).toBe('Move to Peng (dpm)')
    expect(actions[1]!.label).toBe('Keep with Peng')
    expect(actions[3]!.description).toContain('Peng (dpm)\'s page redirects')
  })

  it('names both sides by slug when they share a display name, the usual duplicate', () => {
    const twins = conflict({
      claimant: { ...conflict().claimant, displayName: 'Peng' },
      owner: { ...conflict().owner, displayName: 'peng' },
    })
    expect(conflictActions(twins).map(a => a.label)).toEqual([
      'Move to Peng (peng-dpm)',
      'Keep with peng (peng)',
      'Detach from both',
      'Merge into peng (peng)',
      'Merge into Peng (peng-dpm)',
    ])
  })

  it('asks twice for everything but keep', () => {
    expect(conflictActions(conflict()).filter(a => !a.confirm).map(a => a.key)).toEqual(['keep'])
  })
})

describe('resolvedMessage', () => {
  const base = { id: 'c1', holderSlug: 'peng', mergedSlug: null, alsoResolved: 0 }

  it('says where the account went', () => {
    expect(resolvedMessage({ ...base, resolution: 'Moved', holderSlug: 'peng-dpm' })).toEqual({ title: 'Account moved to peng-dpm', description: undefined })
    expect(resolvedMessage({ ...base, resolution: 'Kept' }).title).toBe('Account kept by peng')
    expect(resolvedMessage({ ...base, resolution: 'Detached', holderSlug: null }).title).toBe('Account detached from both')
  })

  it('names the redirect of a merge and counts what else it settled', () => {
    const message = resolvedMessage({ ...base, resolution: 'Merged', mergedSlug: 'peng-dpm', alsoResolved: 2 })
    expect(message.title).toBe('peng-dpm merged into peng')
    expect(message.description).toBe('/players/peng-dpm now redirects. 2 other conflicts settled with it.')
    expect(resolvedMessage({ ...base, resolution: 'Kept', alsoResolved: 1 }).description).toBe('1 other conflict settled with it.')
  })
})

describe('resolveErrorMessage', () => {
  it('prefers the problem detail, then its title, then a generic line', () => {
    expect(resolveErrorMessage({ data: { detail: 'This conflict is already resolved.' } })).toBe('This conflict is already resolved.')
    expect(resolveErrorMessage({ data: { title: 'Not Found' } })).toBe('Not Found')
    expect(resolveErrorMessage(new Error('boom'))).toBe('The conflict could not be resolved.')
    expect(resolveErrorMessage(null)).toBe('The conflict could not be resolved.')
  })
})
