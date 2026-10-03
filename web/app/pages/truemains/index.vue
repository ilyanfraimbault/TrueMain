<script setup lang="ts">
// The page itself is shared with the desktop app (`layers/common`); only the
// head is the site's.
useSeoMeta({
  title: 'OTP Leaderboard',
  description: 'Leaderboard of tracked one-trick (OTP) League of Legends players, ranked live and filterable by region, role and champion.',
})

useSchemaOrg([
  defineWebPage({ name: 'OTP Leaderboard', description: 'Tracked one-trick League of Legends players ranked by rank.' }),
])
</script>

<template>
  <PageTruemains />
</template>
