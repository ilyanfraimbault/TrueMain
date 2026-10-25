<script setup lang="ts">
// Known players — the operator's view of the people the scrapers group Riot accounts
// under. Two questions: who is listed, with what (the players tab, #2019: list, manual
// add and edit, unlink, removal); which accounts do two known players claim (the
// conflict queue, #2253); and which known players share a name with no account in
// common (the homonyms queue, #2271). `?status=open|resolved|homonyms` deep-links them.
import type { PersonaConflictStatus } from '~~/shared/types/ops'

type Tab = 'Players' | 'Homonyms' | PersonaConflictStatus

const route = useRoute()
const router = useRouter()

function parseTab(raw: unknown): Tab {
  if (raw === 'resolved') {
    return 'Resolved'
  }
  if (raw === 'homonyms') {
    return 'Homonyms'
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
interface HomonymsPanel { refresh: () => void, pending: boolean, total: number }
const conflicts = ref<ConflictsPanel | null>(null)
const directory = ref<DirectoryPanel | null>(null)
const homonyms = ref<HomonymsPanel | null>(null)

const TABS: { value: Tab, label: string, icon: string }[] = [
  { value: 'Players', label: 'Players', icon: 'i-lucide-users' },
  { value: 'Open', label: 'Open conflicts', icon: 'i-lucide-git-pull-request-arrow' },
  { value: 'Resolved', label: 'Resolved conflicts', icon: 'i-lucide-check-check' },
  { value: 'Homonyms', label: 'Homonyms', icon: 'i-lucide-copy' },
]

function label(value: Tab, text: string) {
  if (value === 'Homonyms') {
    return homonyms.value ? `${text} · ${homonyms.value.total}` : text
  }
  const counts = conflicts.value?.counts
  if (value === 'Players' || !counts) {
    return text
  }
  return `${text} · ${value === 'Open' ? counts.open : counts.resolved}`
}

const panel = computed(() => {
  if (tab.value === 'Players') {
    return directory.value
  }
  return tab.value === 'Homonyms' ? homonyms.value : conflicts.value
})
const pending = computed(() => panel.value?.pending ?? false)

function refresh() {
  panel.value?.refresh()
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
      <KnownPlayersHomonyms v-else-if="tab === 'Homonyms'" ref="homonyms" />
      <KnownPlayersConflicts v-else ref="conflicts" v-model:status="status" />
    </template>
  </UDashboardPanel>
</template>
