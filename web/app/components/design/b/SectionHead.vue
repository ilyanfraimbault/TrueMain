<script setup lang="ts">
// A section's heading block: eyebrow, a large lit title (swept by a light as it
// scrolls into view, where the browser supports scroll-driven animations), an
// optional lede, and an aside (usually the "see all" link) set against it.
defineProps<{
  eyebrow: string
  id?: string
}>()
</script>

<template>
  <header class="b-head">
    <div class="b-head__main">
      <p class="b-head__eyebrow">
        {{ eyebrow }}
      </p>
      <h2
        :id="id"
        class="b-head__title b-lit b-lit--scroll"
        data-b-spot
      >
        <slot />
      </h2>
      <p
        v-if="$slots.lede"
        class="b-head__lede"
      >
        <slot name="lede" />
      </p>
    </div>
    <div
      v-if="$slots.aside"
      class="b-head__aside"
    >
      <slot name="aside" />
    </div>
  </header>
</template>
