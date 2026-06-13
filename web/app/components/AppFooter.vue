<script setup lang="ts">
const links = [
  { label: 'Champions', to: '/champions' },
  { label: 'Truemains', to: '/truemains' },
]

const year = new Date().getUTCFullYear()
</script>

<template>
  <UFooter
    :ui="{
      container: 'border-t border-default lg:py-8',
      right: 'gap-x-0 flex-wrap',
    }"
  >
    <template #left>
      <p class="text-sm text-dimmed">
        TrueMain · {{ year }}
      </p>
    </template>

    <template #right>
      <UButton
        v-for="link in links"
        :key="link.label"
        :label="link.label"
        :to="link.to"
        color="neutral"
        variant="link"
        class="font-light"
        size="sm"
      />
    </template>
  </UFooter>
</template>
