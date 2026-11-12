<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'

// One cell of the bento: a link (or a plain box) that owns its grid area, so its
// size never depends on whether its data has arrived — the skeleton and the
// figure share the same box. Its edge catches the cursor light
// (`data-g-spot`, light-engine.ts); its entrance is staggered by `index`.
// `#art` is the full-bleed picture layer under the content.
const props = defineProps<{
  area: string
  index: number
  to?: RouteLocationRaw
  label?: string
  icon?: string
}>()

const NuxtLink = resolveComponent('NuxtLink')
</script>

<template>
  <component
    :is="props.to ? NuxtLink : 'div'"
    :to="props.to"
    class="g-tile"
    :class="[`g-tile--${area}`, { 'is-link': props.to }]"
    :style="{ '--i': index }"
    data-g-spot
  >
    <slot name="art" />
    <div class="g-tile__body">
      <span
        v-if="label"
        class="g-tile__label"
      >
        <UIcon
          v-if="icon"
          :name="icon"
          class="g-tile__icon"
        />
        <span>{{ label }}</span>
        <slot name="label-end" />
      </span>
      <slot />
    </div>
  </component>
</template>
