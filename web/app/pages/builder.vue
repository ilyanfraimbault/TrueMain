<script setup lang="ts">
/**
 * Legacy route of the matchup page (#939). The page moved to `/matchup`; the
 * three matchup inputs are deep-linked, so shared `/builder?champion=…` links
 * are in the wild and must keep resolving — hence a redirect that carries the
 * query over rather than a plain route rule. Excluded from the sitemap
 * (`nuxt.config.ts`) so the old URL isn't advertised alongside the new one.
 */
definePageMeta({
  redirect: to => ({ path: '/matchup', query: to.query }),
})
</script>

<template>
  <div />
</template>
