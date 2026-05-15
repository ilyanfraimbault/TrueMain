<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

const route = useRoute()

const items = computed<NavigationMenuItem[]>(() => [
  {
    label: 'Champions',
    icon: 'i-lucide-swords',
    to: '/champions',
    active: route.path.startsWith('/champions'),
  },
  {
    label: 'Meta',
    icon: 'i-lucide-trending-up',
    to: '/meta',
    active: route.path.startsWith('/meta'),
  },
  {
    label: 'Mains',
    icon: 'i-lucide-users',
    to: '/mains',
    active: route.path.startsWith('/mains'),
  },
  {
    label: 'Patches',
    icon: 'i-lucide-history',
    to: '/patches',
    active: route.path.startsWith('/patches'),
  },
])
</script>

<template>
  <UHeader title="TrueMain">
    <template #title>
      <AppLogo class="text-lg" />
    </template>

    <UNavigationMenu
      :items="items"
      variant="link"
    />

    <template #right>
      <UColorModeButton />
    </template>

    <template #body>
      <UNavigationMenu
        :items="items"
        orientation="vertical"
        class="-mx-2.5"
      />
    </template>
  </UHeader>
</template>
