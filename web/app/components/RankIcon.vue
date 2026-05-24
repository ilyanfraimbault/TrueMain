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

// Lowercase tier name maps directly to the asset filename for every Riot
// tier (iron / bronze / ... / master / grandmaster / challenger). Unknown
// tiers render as a transparent placeholder so the row layout doesn't shift.
const iconUrl = computed(() => {
  const tier = props.tier?.trim().toLowerCase()
  if (!tier) return null
  return `https://raw.communitydragon.org/latest/plugins/rcp-fe-lol-static-assets/global/default/images/ranked-mini-crests/${tier}.png`
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
