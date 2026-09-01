import type { ChampionStaticListItem, RuneTreeResponse } from '~~/shared/types/static-data'
import { RUNE_TREE_KEY_PREFIX, staticFetchKey } from '~/composables/useBuildAssets'

// Warms the static caches that no page has to resolve a patch for —
// `champion-static-list`, and the rune tree under its unresolved-patch key
// (`rune-tree-latest`, which is what `/api/static/rune-tree` with no `?patch=`
// answers) — while the user lands on any route. By the time they navigate to
// /champions the `useAsyncData` calls there hit `getCachedData` and skip the
// round trip.
//
// The rune-tree key is built with the composable's own `staticFetchKey` rather
// than spelled out here: writing the literal is how this warm-up silently went
// dead once (#1231).
//
// Intentionally skips the patch-keyed items map — we'd have to guess which
// patch the user will look at and risk fetching the wrong one. For the same
// reason a page that *has* resolved a patch (`rune-tree-16.15`) still fetches:
// the warm entry only answers the unresolved-patch question.
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

  const runeTreeKey = staticFetchKey(RUNE_TREE_KEY_PREFIX)
  if (!payload[runeTreeKey]) {
    $fetch<RuneTreeResponse>('/api/static/rune-tree')
      .then((data) => {
        payload[runeTreeKey] = data
        markStaticFetched(runeTreeKey, nuxtApp)
      })
      .catch(() => {})
  }
})
