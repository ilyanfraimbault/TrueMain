<script setup lang="ts">
// Clickable champion icon → /champions/{id} (optionally ?position=). The icon
// itself is the link target: a focusable <a> labelled with the champion name,
// so Tab + Enter jump to the build page. Callers pass the already-resolved
// iconUrl + name (both list views cache the champion static list), keeping
// this component free of any data fetching. Auto-registers as <ChampionLink>.
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
}>()

const to = computed(() =>
  props.position
    ? { path: `/champions/${props.championId}`, query: { position: props.position } }
    : `/champions/${props.championId}`)
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
