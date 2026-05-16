<script setup lang="ts">
defineProps<{
  title?: string
}>()
</script>

<template>
  <UCard>
    <template #header>
      <slot name="title">
        <h3 class="text-sm font-medium text-default">
          {{ title }}
        </h3>
      </slot>
    </template>
    <slot />
  </UCard>
</template>
