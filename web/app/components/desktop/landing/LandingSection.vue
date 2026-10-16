<script setup lang="ts">
/**
 * One moment of a game on `/download` (#2072): its eyebrow (the moment), the
 * claim, a line of lead, then whatever the page draws for it. The header rises
 * in once it reaches the viewport — once, never again on the way back up.
 */
defineProps<{ id: string, moment: string, title: string, lead: string }>()

const header = ref<HTMLElement | null>(null)
const seen = useVisibleOnce(header, { rootMargin: '0px 0px -15% 0px' })
const mounted = ref(false)
onMounted(() => (mounted.value = true))
</script>

<template>
  <section :id="id" class="scroll-mt-24 pt-24 sm:pt-32">
    <header ref="header" class="landing-header max-w-2xl" :class="{ 'is-waiting': mounted && !seen }">
      <p class="eyebrow">
        {{ moment }}
      </p>
      <h2 class="mt-3 text-3xl font-semibold leading-tight tracking-tight text-balance text-highlighted sm:text-4xl">
        {{ title }}
      </h2>
      <p class="mt-4 text-base leading-relaxed text-muted text-pretty">
        {{ lead }}
      </p>
    </header>
    <div class="mt-10 sm:mt-12">
      <slot />
    </div>
  </section>
</template>

<style scoped>
.landing-header > * {
  transition: opacity 600ms cubic-bezier(0.2, 0, 0, 1), translate 700ms cubic-bezier(0.2, 0, 0, 1);
}

.landing-header > :nth-child(2) { transition-delay: 70ms; }
.landing-header > :nth-child(3) { transition-delay: 140ms; }

.landing-header.is-waiting > * {
  opacity: 0;
  translate: 0 12px;
}

@media (prefers-reduced-motion: reduce) {
  .landing-header > * { transition: none; }
  .landing-header.is-waiting > * { opacity: 1; translate: none; }
}
</style>
