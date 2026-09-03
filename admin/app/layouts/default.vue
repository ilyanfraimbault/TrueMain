<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'

const open = ref(false)

// Four labelled groups (#1410) rather than one flat list of 15: the portal
// answers three questions — is ingestion flowing, is the data right, what is
// failing — and the sidebar should say so before the operator has to guess which
// page holds the answer. `UNavigationMenu` renders an array of arrays as groups
// with a divider between them; the group labels are the first item of each.
// The three account panels (candidates / explorer / add mains) are one hub with
// tabs, and Riot API is a tab of Processes — those routes still resolve, via
// redirect pages.
const NAV_GROUPS: NavigationMenuItem[][] = [
  [
    { label: 'Monitor', type: 'label' },
    { label: 'Overview', icon: 'i-lucide-layout-dashboard', to: '/' },
    { label: 'Health', icon: 'i-lucide-heart-pulse', to: '/health' },
    { label: 'Processes', icon: 'i-lucide-activity', to: '/processes' },
    { label: 'Logs', icon: 'i-lucide-scroll-text', to: '/logs' },
  ],
  [
    { label: 'Data', type: 'label' },
    { label: 'Champions', icon: 'i-lucide-swords', to: '/champions' },
    { label: 'Aggregation', icon: 'i-lucide-combine', to: '/aggregation' },
    { label: 'Patch Coverage', icon: 'i-lucide-layers', to: '/patch-coverage' },
    { label: 'Data Quality', icon: 'i-lucide-shield-alert', to: '/data-quality' },
  ],
  [
    { label: 'Accounts', type: 'label' },
    { label: 'Accounts', icon: 'i-lucide-users-round', to: '/accounts' },
  ],
  [
    { label: 'System', type: 'label' },
    { label: 'Database', icon: 'i-lucide-database', to: '/database' },
    { label: 'Configuration', icon: 'i-lucide-settings', to: '/configuration' },
    { label: 'Analytics', icon: 'i-lucide-chart-line', to: '/analytics' },
  ],
]

// Closing the mobile drawer is a link concern; the group labels are not links.
const groups = computed<NavigationMenuItem[][]>(() =>
  NAV_GROUPS.map(group => group.map(item => (
    item.type === 'label'
      ? item
      : {
          ...item,
          onSelect: () => {
            open.value = false
          },
        }
  ))),
)
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
          :items="groups"
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
