<script setup lang="ts">
defineProps<{
  title: string
  description: string
  icon?: string
}>()
</script>

<template>
  <UPageHero
    headline="Coming soon"
    :title="title"
    :description="description"
    :ui="{
      container: 'py-24 sm:py-32',
    }"
  >
    <template #top>
      <UIcon
        v-if="icon"
        :name="icon"
        class="size-12 text-primary mx-auto mb-4"
      />
    </template>

    <template #links>
      <UButton
        to="/champions/266"
        color="primary"
        size="lg"
        icon="i-lucide-swords"
        label="Browse a champion"
      />
      <UButton
        to="/"
        color="neutral"
        variant="subtle"
        size="lg"
        icon="i-lucide-arrow-left"
        label="Back home"
      />
    </template>
  </UPageHero>
</template>
