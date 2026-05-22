import type { ChampionStaticListItem, RuneTreeResponse } from '~~/shared/types/static-data'

// Warms the patch-agnostic static caches (`champion-static-list`, `rune-tree`)
// while the user lands on any route. By the time they navigate to /champions
// the `useAsyncData` calls there hit `getCachedData` and skip the round trip.
//
// Intentionally skips the patch-keyed items map — we'd have to guess which
// patch the user will look at and risk fetching the wrong one.
export default defineNuxtPlugin((nuxtApp) => {
  const payload = nuxtApp.payload.data as Record<string, unknown>

  if (!payload['champion-static-list']) {
    $fetch<ChampionStaticListItem[]>('/api/static/champions')
      .then((data) => {
        payload['champion-static-list'] = data
        markStaticFetched('champion-static-list', nuxtApp)
      })
      .catch(() => {
        // Prefetch is best-effort; let the page-level fetch surface errors.
      })
  }

  if (!payload['rune-tree']) {
    $fetch<RuneTreeResponse>('/api/static/rune-tree')
      .then((data) => {
        payload['rune-tree'] = data
        markStaticFetched('rune-tree', nuxtApp)
      })
      .catch(() => {})
  }
})
