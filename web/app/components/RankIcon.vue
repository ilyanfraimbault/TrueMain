<script setup lang="ts">
// Mini rank emblem from Community Dragon. The `latest` channel always
// resolves to the current LoL client assets — same source the project
// already uses for position icons and perk imagery — and IPX caches the
// processed bytes for a week (see nuxt.config.ts).
const props = defineProps<{
  tier: string | null | undefined
  size?: number
}>()

const dim = computed(() => props.size ?? 28)

// Community Dragon's `ranked-mini-crests/` has the clean wing-and-gem
// shape we want — same visual family as the dpm.lol / op.gg leaderboards.
// We use the SVG variants (one file per tier, Emerald included) so the
// icon scales crisply at any size without IPX rasterising a PNG down.
const iconUrl = computed(() => {
  const tier = props.tier?.trim().toLowerCase()
  if (!tier) return null
  return `https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-static-assets/global/default/images/ranked-mini-crests/${tier}.svg`
})
</script>

<template>
  <NuxtImg
    v-if="iconUrl"
    :src="iconUrl"
    :alt="`${tier} rank`"
    :title="tier ?? undefined"
    :width="dim"
    :height="dim"
    class="shrink-0"
    :style="{ width: `${dim}px`, height: `${dim}px` }"
  />
  <div
    v-else
    class="shrink-0 rounded bg-elevated/40"
    :style="{ width: `${dim}px`, height: `${dim}px` }"
    aria-hidden="true"
  />
</template>
