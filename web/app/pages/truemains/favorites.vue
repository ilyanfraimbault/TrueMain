<script setup lang="ts">
// The page itself is shared with the desktop app (`layers/common`); only the
// head is the site's. Per-visitor content: never indexed, and excluded from the
// sitemap in nuxt.config.
useSeoMeta({
  title: 'Favorites',
  description: 'The truemains you follow in this browser, with their latest ranked games.',
})

useRobotsRule('noindex, follow')
</script>

<template>
  <PageFavorites />
</template>
