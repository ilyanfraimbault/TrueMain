<script setup lang="ts">
// Clickable champion icon → the build page: the global `/champions/{slug}` by
// default, or the player-scoped `/truemains/{nameTag}/champions/{slug}` when a
// `nameTag` is given. The icon itself is the link target: a focusable <a>
// labelled with the champion name, so Tab + Enter jump to the build page.
// Callers pass the already-resolved iconUrl + name (both list views cache the
// champion static list), keeping this component free of any data fetching —
// the slug comes from app-wide state (`useChampionSlugs`), read synchronously,
// so that still holds.
// Auto-registers as <ChampionLink>.
//
// Do NOT nest this inside another link/button (e.g. a row that is itself a
// NuxtLink): <a> inside <a> is invalid HTML. Use it only where the icon is a
// standalone target.
const props = defineProps<{
  championId: number
  name: string
  iconUrl?: string | null
  /** When set (and non-empty), deep-links the build page's lane filter. */
  position?: string | null
  /**
   * When set, links to this truemain's player-scoped build page
   * (`/truemains/{nameTag}/champions/{slug}`) instead of the global one. The
   * player slug is `{gameName}-{tagLine}` and is URL-encoded for us. Takes
   * precedence over `position` (the scoped page has no lane query).
   */
  nameTag?: string | null
}>()

const { pathFor, truemainPathFor } = useChampionSlugs()

const to = computed(() => {
  if (props.nameTag)
    return truemainPathFor(props.nameTag, props.championId)
  return props.position
    ? { path: pathFor(props.championId), query: { position: props.position } }
    : pathFor(props.championId)
})
</script>

<template>
  <NuxtLink
    :to="to"
    :aria-label="name"
    class="inline-flex shrink-0 overflow-hidden rounded focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
  >
    <SkeletonImage
      :src="iconUrl"
      :alt="name"
      class="size-full"
    />
  </NuxtLink>
</template>
