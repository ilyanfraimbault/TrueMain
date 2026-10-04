type ChampionFilters = ReturnType<typeof useChampionFilters>['filters']

/**
 * Everything the champion detail page says *about itself*: `<head>` meta, the
 * dynamic og:image, schema.org, the share copy and the visible breadcrumb.
 * Every string is built from the SSR-safe `seoDisplayName`
 * (`useChampionSeoName`), never the client-only `displayName`, so the server
 * HTML carries the champion name rather than `Champion {id}`.
 */
export function useChampionPageSeo(options: {
  championId: ComputedRef<number>
  filters: ChampionFilters
  seoDisplayName: ComputedRef<string | null>
  seoPositionLabel: ComputedRef<string | undefined>
}) {
  const { championId, filters, seoDisplayName, seoPositionLabel } = options

  useSeoMeta({
    title: () => seoDisplayName.value
      ? `${seoDisplayName.value}${seoPositionLabel.value ? ` ${seoPositionLabel.value}` : ''} Build`
      : `Champion ${championId.value} Build`,
    description: () => seoDisplayName.value
      ? `${seoDisplayName.value} build guide: best runes, items and skill order`
        + `${seoPositionLabel.value ? ` for ${seoPositionLabel.value}` : ''}, based on real ranked games. `
        + `See the top OTP ${seoDisplayName.value} one-tricks on TrueMain.`
      : `Champion builds, runes and skill order from true main players.`,
  })

  // Copy shown in the native share sheet and as the X post text. Built from the
  // same SSR-safe display name the title uses, so it never reads "Champion 103".
  const shareTitle = computed(() =>
    seoDisplayName.value
      ? `${seoDisplayName.value}${seoPositionLabel.value ? ` ${seoPositionLabel.value}` : ''} build on TrueMain`
      : 'Champion build on TrueMain',
  )
  const shareDescription = computed(() =>
    seoDisplayName.value
      ? `Runes, items and skill order for ${seoDisplayName.value}, from real one-tricks.`
      : 'Runes, items and skill orders from true main players.',
  )

  // Dynamic share card (#926). Only *identifiers* are handed over — the champion
  // id plus whatever slice the shared URL pinned. Everything the page renders is
  // fetched `server: false` (the #149 hydration fix), so at SSR — the moment this
  // og:image URL is minted — there is not a single number available to pass. The
  // card therefore resolves its own slice through `/api/og/champion/{id}` when a
  // crawler renders it, which also keeps the extra query off the human page-view
  // path. Props are resolved once (no client-side follow-up), but filters live in
  // the query string, so a crawler's SSR render of a copied link gets its card.
  defineOgImage('Champion', {
    championId,
    position: computed(() => filters.value.position ?? undefined),
    eloBracket: computed(() => filters.value.eloBracket ?? undefined),
    patch: computed(() => filters.value.patch ?? undefined),
  })

  useSchemaOrg([
    defineWebPage({
      name: () => seoDisplayName.value ? `${seoDisplayName.value} Build` : undefined,
      description: () => `${seoDisplayName.value ?? 'Champion'} runes, items and skill order.`,
    }),
    defineBreadcrumb({
      itemListElement: [
        { name: 'Champions', item: '/champions' },
        { name: () => seoDisplayName.value ?? `Champion ${championId.value}` },
      ],
    }),
  ])

  // Visible breadcrumb, mirroring the schema.org hierarchy above.
  const breadcrumbItems = computed(() => [
    { label: 'Champions', to: '/champions' },
    { label: seoDisplayName.value ?? `Champion ${championId.value}` },
  ])

  return { shareTitle, shareDescription, breadcrumbItems }
}
