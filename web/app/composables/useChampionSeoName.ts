import type { ChampionStaticData } from '~~/shared/types/static-data'

/**
 * SSR-safe champion display name for `<head>`, shared by the global champion
 * page and its player-scoped twin (`pages/champions/[slug].vue` and
 * `pages/truemains/[nameTag]/champions/[slug].vue`).
 *
 * Meta-only fetch: the `displayName` both pages render comes from client-only
 * (`server: false`) statics, chosen to avoid hydration mismatches on the visual
 * build content, which means it is always null during SSR — the exact HTML
 * Google indexes would permanently read "Champion {id}" instead of the champion
 * name. `<head>` tags aren't part of Vue's DOM diff, so a dedicated SSR-enabled
 * fetch here carries no hydration risk. It hits the same
 * `defineCachedEventHandler` (1h TTL) as `useChampionStatic`, so it's a cache
 * hit, not an extra DDragon round trip.
 *
 * Awaited server-side only — hence the `async` signature: the app has no
 * NuxtLoadingIndicator/Suspense fallback on `<NuxtPage>`, so awaiting on the
 * client would freeze the outgoing page with no feedback on every client-side
 * champion navigation, purely for a `<head>`-only value.
 *
 * The patch travels as a **reactive** query, not a value read once at setup.
 * `selectedPatch` starts empty and resolves a tick later; unwrapping it into a
 * plain object literal would freeze the request on the unpatched slice while
 * the key — which carries the patch — still flipped, so a
 * `champion-seo-name-103-16.15` entry would hold the `none`-patch response.
 * Nuxt unwraps refs inside `query` and watches them, so a `ComputedRef` is the
 * form that actually re-issues the request (a bare getter is neither unwrapped
 * nor tracked, and would be serialised into the URL as-is).
 */
export async function useChampionSeoName(
  championId: MaybeRefOrGetter<number>,
  patch: MaybeRefOrGetter<string | null | undefined>,
  fallbackName: MaybeRefOrGetter<string | null | undefined>,
) {
  const championIdRef = computed(() => toValue(championId))
  const patchRef = computed(() => toValue(patch) || undefined)

  const seoStaticFetch = useFetch<ChampionStaticData>(
    () => `/api/static/${championIdRef.value}`,
    {
      key: () => `champion-seo-name-${championIdRef.value}-${patchRef.value ?? 'none'}`,
      query: computed(() => ({ patch: patchRef.value })),
    },
  )
  if (import.meta.server) await seoStaticFetch

  const seoDisplayName = computed<string | null>(
    () => seoStaticFetch.data.value?.championName ?? toValue(fallbackName) ?? null,
  )

  return { seoDisplayName }
}
