<script setup lang="ts">
// The page itself is shared with the desktop app (`layers/common`); only the
// head is the site's.
useSeoMeta({
  title: 'Matchup',
  description:
    'Pick your champion and your role opponent — the recommended build rebuilds live from real '
    + 'games of that exact matchup, refined by the rest of the draft.',
})
</script>

<template>
  <PageMatchup />
</template>
