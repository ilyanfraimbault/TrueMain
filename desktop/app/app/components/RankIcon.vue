<!--
  Twin of `web/app/components/RankIcon.vue`, minus the `_ipx` URL: the app has
  no image server, so Community Dragon's SVG crest is used as given (the CSP
  lets images through from raw.communitydragon.org).
-->
<script setup lang="ts">
const props = defineProps<{
  tier: string | null | undefined
  size?: number
  loading?: 'lazy' | 'eager'
}>()

const dim = computed(() => props.size ?? 28)

const iconUrl = computed(() => {
  const tier = props.tier?.trim().toLowerCase()
  if (!tier) return null
  return `https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-static-assets/global/default/images/ranked-mini-crests/${tier}.svg`
})
</script>

<template>
  <img
    v-if="iconUrl"
    :src="iconUrl"
    :alt="`${tier} rank`"
    :title="tier ?? undefined"
    :width="dim"
    :height="dim"
    :loading="loading"
    class="shrink-0"
    :style="{ width: `${dim}px`, height: `${dim}px` }"
  >
  <div
    v-else
    class="shrink-0 rounded bg-elevated/40"
    :style="{ width: `${dim}px`, height: `${dim}px` }"
    aria-hidden="true"
  />
</template>
