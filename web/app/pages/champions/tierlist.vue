<script setup lang="ts">
// The page itself is shared with the desktop app (`layers/common`); only the
// head is the site's.
useSeoMeta({
  title: 'Champion Tier List',
  description: 'Champion meta tier list ranking champions by winrate and pickrate per role for the current patch.',
})

useSchemaOrg([
  defineWebPage({ name: 'Champion Tier List' }),
])
</script>

<template>
  <PageTierList />
</template>
