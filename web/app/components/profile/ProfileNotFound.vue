<script setup lang="ts">
defineProps<{
  nameTag: string
}>()
</script>

<template>
  <div class="glass rounded-lg px-6 py-12 text-center">
    <p class="text-base font-semibold">
      Player not found
    </p>
    <p class="mt-1 text-sm text-muted">
      No tracked truemain matches <code>{{ nameTag }}</code>. The Riot ID should be
      <code>GameName-TagLine</code>.
    </p>
  </div>
</template>
