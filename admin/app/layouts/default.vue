<script setup lang="ts">
import type { NavigationMenuItem } from '@nuxt/ui'
import { buildSearchGroups, NAV_GROUPS } from '~/utils/navigation'
import { createOpsPrefetcher, hoveredRoute } from '~/utils/ops-cache'

const prefetch = createOpsPrefetcher((url, query) => $fetch(url, { query }))

function warm(event: Event) {
  const route = hoveredRoute(event.target)
  if (route) {
    void prefetch(route)
  }
}

// Bound in script rather than as template handlers: the wrapper is not itself
// interactive, its links are, and keyboard focus is covered by `focusin`.
const navWrapper = useTemplateRef<HTMLElement>('navWrapper')
useEventListener(navWrapper, ['mouseover', 'focusin'], warm)

const open = ref(false)

// Sidebar groups and the ⌘K palette's groups live in `utils/navigation.ts`.
const searchGroups = buildSearchGroups()

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
        <UDashboardSearchButton :collapsed="collapsed" tooltip class="mb-2" />

        <!-- Hovering or focusing an entry warms its page's main reads (#2306). Listened
             on the wrapper — the events bubble from the menu's own links. -->
        <div ref="navWrapper">
          <UNavigationMenu
            :collapsed="collapsed"
            :items="groups"
            orientation="vertical"
            tooltip
            popover
          />
        </div>
      </template>

      <template #footer="{ collapsed }">
        <UserMenu :collapsed="collapsed" />
      </template>
    </UDashboardSidebar>

    <!-- ⌘K anywhere in the portal. The app runs `ssr: false`, so the shortcut is
         bound on the client at mount like any other listener. The portal is
         dark-only (no theme switch anywhere), so the palette's built-in
         light/dark commands are dropped rather than offering a dead end. -->
    <UDashboardSearch
      :groups="searchGroups"
      :color-mode="false"
      placeholder="Jump to a page…"
      title="Search pages"
      description="Jump to any page of the admin portal."
    />

    <slot />

    <OpsCacheAgeNotice />
  </UDashboardGroup>
</template>
