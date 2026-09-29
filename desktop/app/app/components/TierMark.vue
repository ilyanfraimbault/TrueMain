<script setup lang="ts">
/**
 * A tier as a pick card carries it: the letter in the site's tier colour
 * (`--color-tier-*`, the medal ladder), "tier" under it. The tier itself is the
 * site's, computed server-side — this only draws it.
 */
const props = defineProps<{ tier: string }>()

const letter = computed(() => props.tier.toUpperCase())
const known = computed(() => ['S', 'A', 'B', 'C', 'D'].includes(letter.value))
</script>

<template>
  <span
    v-if="known"
    class="flex flex-col items-start leading-none"
    :style="{ color: `var(--color-tier-${letter.toLowerCase()})` }"
    :aria-label="`Tier ${letter}`"
  >
    <span class="font-mono text-[22px] font-bold tracking-tight drop-shadow-[0_1px_2px_rgb(0_0_0/0.8)]">{{ letter }}</span>
    <span class="mt-0.5 text-[9px] font-medium uppercase tracking-[0.14em] text-default/80">tier</span>
  </span>
</template>
