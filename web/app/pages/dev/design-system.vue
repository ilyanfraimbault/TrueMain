<script setup lang="ts">
/**
 * Design-system review page — the project's stand-in for a component gallery.
 *
 * There is no Storybook here and `web/vitest.config.ts` deliberately omits
 * `@vitejs/plugin-vue`, so no test in this app can mount an SFC and no snapshot
 * can catch a token regression. This page is the compensating control: it puts
 * every foundation on one screen so a palette or elevation change can be judged
 * in a single screenshot instead of by touring the real pages and hoping the
 * affected state was on one of them.
 *
 * Like the other `pages/dev/*` playgrounds it is stripped from production
 * builds by the `pages:extend` hook in `nuxt.config.ts`.
 */

useSeoMeta({
  title: 'Design system playground',
  description: 'Isolated visual review of the app’s colour, elevation and type foundations.',
})
</script>

<template>
  <UContainer class="flex flex-col gap-10 py-10">
    <PageHeader
      eyebrow="Playground"
      title="Design system"
      description="Every foundation on one screen. Judge a token change here before touring the real pages."
    />

    <DevDesignSystemColours />
    <DevDesignSystemSurfaces />
    <DevDesignSystemType />
  </UContainer>
</template>
