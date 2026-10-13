/**
 * Everything the player-scoped champion page says *about itself*: `<head>`
 * meta, schema.org and the visible breadcrumb (Truemains > {player} >
 * {champion}). The champion crumb uses the SSR-safe `seoDisplayName`
 * (`useChampionSeoName`), as the global page's `useChampionPageSeo` does.
 */
export function usePlayerChampionPageSeo(options: {
  championId: ComputedRef<number>
  seoDisplayName: ComputedRef<string | null>
  playerLabel: ComputedRef<string>
  profilePath: ComputedRef<string>
}) {
  const { championId, seoDisplayName, playerLabel, profilePath } = options

  const breadcrumbItems = computed(() => [
    { label: 'Truemains', to: '/truemains' },
    { label: playerLabel.value, to: profilePath.value },
    { label: seoDisplayName.value ?? `Champion ${championId.value}` },
  ])

  useSeoMeta({
    title: () => `${seoDisplayName.value ?? `Champion ${championId.value}`} Build by ${playerLabel.value}`,
    description: () => `${playerLabel.value}'s ${seoDisplayName.value ?? `champion ${championId.value}`} build: `
      + `runes, items and skill order from their real ranked games as a ${seoDisplayName.value ?? 'this champion'} OTP.`,
  })

  useSchemaOrg([
    defineWebPage({
      name: () => `${seoDisplayName.value ?? 'Champion'} Build by ${playerLabel.value}`,
    }),
    defineBreadcrumb({
      itemListElement: [
        { name: 'Truemains', item: '/truemains' },
        { name: playerLabel.value, item: profilePath.value },
        { name: () => seoDisplayName.value ?? `Champion ${championId.value}` },
      ],
    }),
  ])

  return { breadcrumbItems }
}
