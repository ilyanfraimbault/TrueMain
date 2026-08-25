import { describe, expect, it } from 'vitest'
import { resolveChampionStaticPatch } from '~/utils/champion-patch'

const LATEST = '15.16.1'

describe('resolveChampionStaticPatch', () => {
  it('prefers the loaded champion patch over everything else', () => {
    expect(resolveChampionStaticPatch({
      championPatch: '15.14',
      filterPatch: '15.10',
      latestVersion: LATEST,
      championSettled: true,
    })).toBe('15.14')
  })

  it('falls back to the URL filter while the champion has no patch', () => {
    expect(resolveChampionStaticPatch({
      championPatch: null,
      filterPatch: '15.10',
      latestVersion: LATEST,
      championSettled: false,
    })).toBe('15.10')
  })

  it('stays null while the champion fetch is still in flight', () => {
    // The deferred static fetches (#817) key off this: resolving to `latest`
    // here would fetch the large payloads under one key and refetch them under
    // the champion's patch a moment later.
    expect(resolveChampionStaticPatch({
      championPatch: null,
      filterPatch: null,
      latestVersion: LATEST,
      championSettled: false,
    })).toBeNull()
  })

  it('falls back to the latest version once the fetch settled without a patch', () => {
    // #1211: a player with no build aggregate 404s, so no patch is ever
    // coming. Returning null here left the static fetches parked in `idle`,
    // which pinned the page's loading bar on and suppressed both the build
    // region and the no-build notice.
    expect(resolveChampionStaticPatch({
      championPatch: null,
      filterPatch: null,
      latestVersion: LATEST,
      championSettled: true,
    })).toBe(LATEST)
  })

  it('stays null when settled and DDragon gave us no version either', () => {
    // `useDDragonVersions` defaults to `[]`, so this is what a DDragon outage
    // looks like. Null is correct — there is no patch to pin to — and the page
    // has bigger problems than the build breakdown at that point.
    expect(resolveChampionStaticPatch({
      championPatch: null,
      filterPatch: null,
      latestVersion: undefined,
      championSettled: true,
    })).toBeNull()
  })

  it('treats empty strings as absent, not as a patch', () => {
    // `filters.value.patch` is `''` (not null) for an unset filter.
    expect(resolveChampionStaticPatch({
      championPatch: '',
      filterPatch: '',
      latestVersion: LATEST,
      championSettled: true,
    })).toBe(LATEST)
  })
})
