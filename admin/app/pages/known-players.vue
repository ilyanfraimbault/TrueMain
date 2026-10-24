<script setup lang="ts">
// Known players — the operator's view of the people the scrapers group Riot accounts
// under. Two questions: who is listed, with what (the players tab, #2019: list, manual
// add and edit, unlink, removal); and which accounts do two known players claim (the
// conflict queue, #2253). `?status=open|resolved` deep-links the queue.
import type { PersonaConflictStatus } from '~~/shared/types/ops'

type Tab = 'Players' | PersonaConflictStatus

const route = useRoute()
const router = useRouter()

function parseTab(raw: unknown): Tab {
  if (raw === 'resolved') {
    return 'Resolved'
  }
  return raw === 'open' ? 'Open' : 'Players'
}

const tab = ref<Tab>(parseTab(route.query.status))
const status = computed<PersonaConflictStatus>({
  get: () => (tab.value === 'Resolved' ? 'Resolved' : 'Open'),
  set: (value) => { tab.value = value },
})
watch(tab, (value) => {
  // Each tab keeps its own deep link: `?player=` for the players, `?conflict=` for the queue.
  const { conflict, player, ...rest } = route.query
  router.replace({
    query: value === 'Players'
      ? { ...rest, status: undefined, player }
      : { ...rest, conflict, status: value.toLowerCase() },
  })
})
watch(() => route.query.status, (raw) => {
  tab.value = parseTab(raw)
})

interface ConflictsPanel { refresh: () => void, pending: boolean, counts: { open: number, resolved: number } }
interface DirectoryPanel { refresh: () => void, pending: boolean }
const conflicts = ref<ConflictsPanel | null>(null)
const directory = ref<DirectoryPanel | null>(null)

const TABS: { value: Tab, label: string, icon: string }[] = [
  { value: 'Players', label: 'Players', icon: 'i-lucide-users' },
  { value: 'Open', label: 'Open conflicts', icon: 'i-lucide-git-pull-request-arrow' },
  { value: 'Resolved', label: 'Resolved conflicts', icon: 'i-lucide-check-check' },
]

function label(value: Tab, text: string) {
  const counts = conflicts.value?.counts
  if (value === 'Players' || !counts) {
    return text
  }
  return `${text} · ${value === 'Open' ? counts.open : counts.resolved}`
}

const pending = computed(() => (tab.value === 'Players' ? directory.value?.pending : conflicts.value?.pending) ?? false)

function refresh() {
  if (tab.value === 'Players') {
    directory.value?.refresh()
  }
  else {
    conflicts.value?.refresh()
  }
}
</script>

<template>
  <UDashboardPanel id="known-players">
    <template #header>
      <UDashboardNavbar title="Known players" icon="i-lucide-badge-check">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="pending"
            aria-label="Refresh"
            @click="refresh()"
          />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar>
        <template #left>
          <div class="flex items-center gap-1">
            <UButton
              v-for="item in TABS"
              :key="item.value"
              :color="tab === item.value ? 'primary' : 'neutral'"
              :variant="tab === item.value ? 'solid' : 'ghost'"
              :icon="item.icon"
              :label="label(item.value, item.label)"
              @click="tab = item.value"
            />
          </div>
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <KnownPlayersDirectory v-if="tab === 'Players'" ref="directory" />
      <KnownPlayersConflicts v-else ref="conflicts" v-model:status="status" />
    </template>
  </UDashboardPanel>
</template>
