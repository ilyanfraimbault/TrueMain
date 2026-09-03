<script setup lang="ts">
// Retired route (#1410): Riot API usage is the "Riot API" tab of `/processes` —
// it reports how the pipeline spends its call budget, which is a pipeline signal
// rather than a page of its own. Kept as a redirect so bookmarks still resolve.
definePageMeta({
  redirect: to => ({ path: '/processes', query: { ...to.query, view: 'riot-api' } }),
})
</script>

<template>
  <div />
</template>
