<script setup lang="ts">
defineProps<{
  nameTag: string
}>()
</script>

<template>
  <!--
    An empty state, not the 404 page (#1669). The Riot ID may well name a real
    player we simply do not track — "we don't hold this" is an answer, not a
    failure — so the page keeps its breadcrumb and its shareable URL, and the
    card offers a way onward instead of `error.vue`'s single way back.
  -->
  <UEmpty
    icon="i-lucide-user-round-search"
    title="Player not found"
  >
    <template #description>
      <!-- `whitespace-nowrap`: the description is `text-balance`, which was
           splitting `Rabble ssArouser-EUW` at its own hyphen across two lines.
           A Riot ID is one token to a reader trying to spot their typo. -->
      No tracked truemain matches <code class="whitespace-nowrap">{{ nameTag }}</code>. The Riot ID should be
      <code class="whitespace-nowrap">GameName-TagLine</code>.
    </template>

    <template #actions>
      <UButton
        to="/truemains"
        color="primary"
        variant="soft"
        icon="i-lucide-trophy"
        label="Browse the leaderboard"
      />
    </template>
  </UEmpty>
</template>
