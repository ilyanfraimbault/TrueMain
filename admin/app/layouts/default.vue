<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

const open = ref(false)

// Single flat group of operator panels. Each later panel is filled in by a
// dedicated page under `app/pages/`; the labels/icons here are the source of
// truth for the sidebar.
const items = computed<NavigationMenuItem[]>(() => [
  { label: 'Overview', icon: 'i-lucide-layout-dashboard', to: '/' },
  { label: 'Champions', icon: 'i-lucide-swords', to: '/champions' },
  { label: 'Database', icon: 'i-lucide-database', to: '/database' },
  { label: 'Data Quality', icon: 'i-lucide-shield-alert', to: '/data-quality' },
  { label: 'Candidates', icon: 'i-lucide-users-round', to: '/candidates' },
  { label: 'Processes', icon: 'i-lucide-activity', to: '/processes' },
  { label: 'Logs', icon: 'i-lucide-scroll-text', to: '/logs' },
  { label: 'Riot API', icon: 'i-lucide-gauge', to: '/riot-api' },
  { label: 'Analytics', icon: 'i-lucide-chart-line', to: '/analytics' },
  { label: 'Add mains', icon: 'i-lucide-user-plus', to: '/seed' },
].map(item => ({
  ...item,
  onSelect: () => {
    open.value = false
  },
})))
</script>

<template>
  <UDashboardGroup unit="rem">
    <UDashboardSidebar
      id="default"
      v-model:open="open"
      collapsible
      resizable
      class="bg-elevated/25"
      :ui="{ footer: 'lg:border-t lg:border-default' }"
    >
      <template #header="{ collapsed }">
        <div
          class="flex items-center gap-2 w-full"
          :class="collapsed ? 'justify-center' : ''"
        >
          <UIcon name="i-lucide-shield" class="size-6 shrink-0 text-primary" />
          <span v-if="!collapsed" class="font-semibold text-highlighted truncate">
            TrueMain Admin
          </span>
        </div>
      </template>

      <template #default="{ collapsed }">
        <UNavigationMenu
          :collapsed="collapsed"
          :items="items"
          orientation="vertical"
          tooltip
          popover
        />
      </template>

      <template #footer="{ collapsed }">
        <UserMenu :collapsed="collapsed" />
      </template>
    </UDashboardSidebar>

    <slot />
  </UDashboardGroup>
</template>
