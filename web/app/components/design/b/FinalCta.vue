<script setup lang="ts">
// The closing call: a stage light switches on above the headline the first
// time the section is well in view (a short flicker, then steady), and the two
// destinations pull toward the cursor. Under reduced motion the light is simply on.
const sectionEl = ref<HTMLElement | null>(null)
const on = ref(false)
let observer: IntersectionObserver | null = null

onMounted(() => {
  if (!sectionEl.value) return
  observer = new IntersectionObserver(([entry]) => {
    if (!entry?.isIntersecting) return
    on.value = true
    observer?.disconnect()
  }, { threshold: 0.45 })
  observer.observe(sectionEl.value)
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <section
    ref="sectionEl"
    class="b-final"
    :class="{ 'is-on': on }"
    aria-labelledby="b-final-title"
  >
    <span
      class="b-final__beam"
      aria-hidden="true"
    />
    <span
      class="b-final__floor"
      aria-hidden="true"
    />
    <div class="b-final__inner">
      <p class="b-head__eyebrow">
        Your champion
      </p>
      <h2
        id="b-final-title"
        class="b-final__title b-lit"
        data-b-spot
      >
        Find <span
          class="b-lit--rose"
          data-b-spot
        >your</span> real build.
      </h2>
      <p class="b-final__lede">
        Open the champion you actually play and see what their mains are buying this patch.
      </p>
      <div class="b-final__ctas">
        <UButton
          v-magnetic="0.35"
          to="/champions"
          size="xl"
          icon="i-lucide-swords"
          label="Explore champions"
          class="b-cta"
        />
        <UButton
          v-magnetic="0.35"
          to="/truemains"
          color="neutral"
          variant="subtle"
          size="xl"
          icon="i-lucide-trophy"
          label="Truemains leaderboard"
          class="b-cta b-cta--ghost"
        />
      </div>
      <ClientOnly>
        <p class="b-final__kbd">
          or press
          <UKbd
            value="meta"
            class="font-mono"
          />
          <UKbd
            value="K"
            class="font-mono"
          />
          to search anything
        </p>
      </ClientOnly>
    </div>
  </section>
</template>
