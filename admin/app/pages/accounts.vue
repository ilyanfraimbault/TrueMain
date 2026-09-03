<script setup lang="ts">
// Accounts hub (#1410) — one page for the three panels that all answer the same
// subject, "what is happening to the accounts entering the pipeline":
//   - Pipeline: the `main_candidates` funnel and its throughput (was `/candidates`);
//   - Trace:    one Riot ID walked through every stage (was `/accounts`);
//   - Add mains: the single/bulk seed forms and the seed-request queue (was `/seed`).
// The three used to be three sidebar entries, which made the operator pick a page
// before knowing which one held the answer.
//
// Tabs are deep-linkable via `?view=pipeline|trace|seed`, the pattern `/logs`
// already uses, so the retired routes redirect onto a tab instead of breaking a
// bookmark. Each panel keeps its own filters and its own fetches, and exposes
// `refresh`/`pending` so the one navbar button below drives the open tab.

type AccountsView = 'pipeline' | 'trace' | 'seed'

const TABS: { value: AccountsView, label: string, icon: string }[] = [
  { value: 'pipeline', label: 'Pipeline', icon: 'i-lucide-users-round' },
  { value: 'trace', label: 'Trace', icon: 'i-lucide-user-search' },
  { value: 'seed', label: 'Add mains', icon: 'i-lucide-user-plus' },
]

const route = useRoute()
const router = useRouter()

function parseView(raw: unknown): AccountsView {
  return raw === 'trace' || raw === 'seed' ? raw : 'pipeline'
}

const view = ref<AccountsView>(parseView(route.query.view))
watch(view, (value) => {
  // `pipeline` is the default, so it stays out of the URL.
  router.replace({
    query: { ...route.query, view: value === 'pipeline' ? undefined : value },
  })
})

// One ref for all three panels: only the open tab is rendered, so at most one is
// ever bound. `defineExpose` hands back a proxy that unwraps refs, which is what
// keeps the button's spinner reactive.
interface AccountsPanel { refresh: () => void, pending: boolean }
const panel = ref<AccountsPanel | null>(null)
</script>

<template>
  <UDashboardPanel id="accounts">
    <template #header>
      <UDashboardNavbar title="Accounts" icon="i-lucide-users-round">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="panel?.pending ?? false"
            aria-label="Refresh"
            @click="panel?.refresh()"
          />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <div class="flex items-center gap-1">
            <UButton
              v-for="tab in TABS"
              :key="tab.value"
              :color="view === tab.value ? 'primary' : 'neutral'"
              :variant="view === tab.value ? 'solid' : 'ghost'"
              :icon="tab.icon"
              :label="tab.label"
              @click="view = tab.value"
            />
          </div>
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <AccountsTrace v-if="view === 'trace'" ref="panel" />
      <AccountsSeed v-else-if="view === 'seed'" ref="panel" />
      <AccountsPipeline v-else ref="panel" />
    </template>
  </UDashboardPanel>
</template>
