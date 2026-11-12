<script setup lang="ts">
/**
 * Design exploration, variant C — the last scene before the credits: one line
 * in giant type, half solid, half drawn in outline that fills with rose gold
 * as it scrolls through the viewport, and the two ways in.
 */
</script>

<template>
  <section
    class="c-finale"
    aria-labelledby="c-finale-title"
  >
    <p
      v-reveal="{ effect: 'blur' }"
      class="c-mono c-finale__kicker"
    >
      Your champion
    </p>
    <h2
      id="c-finale-title"
      v-reveal.stagger="{ effect: 'c-mask', step: 120 }"
      class="c-finale__title"
    >
      <span class="c-mask"><span>Find your</span></span>{{ ' ' }}
      <span class="c-mask"><span class="c-finale__outline">real build.</span></span>
    </h2>
    <p
      v-reveal="{ delay: 200, effect: 'blur' }"
      class="c-finale__lede"
    >
      Open the champion you actually play and see what their mains are buying this patch.
    </p>
    <div
      v-reveal="{ delay: 320 }"
      class="c-finale__actions"
    >
      <UButton
        v-magnetic="0.3"
        to="/champions"
        size="xl"
        icon="i-lucide-swords"
        label="Explore champions"
        class="c-btn-primary"
      />
      <UButton
        to="/truemains"
        color="neutral"
        variant="ghost"
        size="xl"
        trailing-icon="i-lucide-arrow-right"
        label="Truemains leaderboard"
        class="c-btn-ghost"
      />
    </div>
  </section>
</template>
