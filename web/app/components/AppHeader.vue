<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

const route = useRoute()

function isActive(prefix: string): boolean {
  return route.path === prefix || route.path.startsWith(`${prefix}/`)
}

const items = computed<NavigationMenuItem[]>(() => [
  {
    label: 'Champions',
    icon: 'i-lucide-swords',
    to: '/champions',
    active: isActive('/champions'),
  },
  {
    label: 'Truemains',
    icon: 'i-lucide-trophy',
    to: '/truemains',
    // Exact match only — the player profile pages (/truemains/{nameTag})
    // shouldn't light up the leaderboard entry in the nav.
    active: route.path === '/truemains',
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
      <AppSearch variant="button" shortcut />
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
