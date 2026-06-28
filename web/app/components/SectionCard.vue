<script setup lang="ts">
defineProps<{
  title?: string
  subtitle?: string
}>()
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="flex min-w-0 flex-col gap-0.5">
          <slot name="title">
            <h3 class="text-sm font-medium text-default">
              {{ title }}
            </h3>
          </slot>
          <p
            v-if="subtitle"
            class="text-xs text-muted"
          >
            {{ subtitle }}
          </p>
        </div>
        <slot name="actions" />
      </div>
    </template>
    <slot />
  </UCard>
</template>
