<script setup lang="ts">
// The page itself is shared with the desktop app (`layers/common`); only the
// head is the site's.
useSeoMeta({
  title: 'Champion Builds',
  description: 'Browse every champion build by lane — most-played runes, items and skill order, winrate and pickrate for the current patch.',
})

useSchemaOrg([
  defineWebPage({ name: 'Champion Builds' }),
])
</script>

<template>
  <PageChampions />
</template>
