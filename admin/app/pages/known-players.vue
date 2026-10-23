<script setup lang="ts">
// Known players (#2253) — the operator's view of the people the scrapers group Riot
// accounts under. Today one question: which accounts do two known players claim, and
// who should have them. `?status=resolved` deep-links the history.
import type { PersonaConflictStatus } from '~~/shared/types/ops'

const route = useRoute()
const router = useRouter()

function parseStatus(raw: unknown): PersonaConflictStatus {
  return raw === 'resolved' ? 'Resolved' : 'Open'
}

const status = ref<PersonaConflictStatus>(parseStatus(route.query.status))
watch(status, (value) => {
  router.replace({ query: { ...route.query, status: value === 'Open' ? undefined : 'resolved' } })
})
watch(() => route.query.status, (raw) => {
  status.value = parseStatus(raw)
})

interface ConflictsPanel { refresh: () => void, pending: boolean, counts: { open: number, resolved: number } }
const panel = ref<ConflictsPanel | null>(null)

const TABS: { value: PersonaConflictStatus, label: string, icon: string }[] = [
  { value: 'Open', label: 'Open', icon: 'i-lucide-git-pull-request-arrow' },
  { value: 'Resolved', label: 'Resolved', icon: 'i-lucide-check-check' },
]

function count(value: PersonaConflictStatus) {
  const counts = panel.value?.counts
  return counts ? (value === 'Open' ? counts.open : counts.resolved) : null
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
              :color="status === tab.value ? 'primary' : 'neutral'"
              :variant="status === tab.value ? 'solid' : 'ghost'"
              :icon="tab.icon"
              :label="count(tab.value) === null ? `${tab.label} conflicts` : `${tab.label} conflicts · ${count(tab.value)}`"
              @click="status = tab.value"
            />
          </div>
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <KnownPlayersConflicts ref="panel" v-model:status="status" />
    </template>
  </UDashboardPanel>
</template>
