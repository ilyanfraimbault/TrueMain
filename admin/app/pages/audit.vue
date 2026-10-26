<script setup lang="ts">
// Audit — who did what, when (#2288): the operator-action trail the known-player
// edits, the conflict and homonym decisions and the seed requests write, newest
// first and paged. Filters and fetch live in `useAuditList`.
const {
  action,
  actor,
  targetType,
  targetId,
  sinceWindow,
  searchInput,
  page,
  actionItems,
  actorItems,
  targetTypeItems,
  hasActiveFilters,
  clearTarget,
  resetFilters,
  pending,
  error,
  refresh,
  entries,
  total,
  serverPageSize,
} = useAuditList()
</script>

<template>
  <UDashboardPanel id="audit">
    <template #header>
      <UDashboardNavbar title="Audit" icon="i-lucide-history">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <UButton icon="i-lucide-refresh-cw" color="neutral" variant="ghost" :loading="pending" aria-label="Refresh" @click="refresh()" />
        </template>
      </UDashboardNavbar>

      <UDashboardToolbar :ui="{ root: 'h-auto py-2' }">
        <div class="flex flex-wrap items-center gap-2 w-full">
          <USelect v-model="action" :items="actionItems" icon="i-lucide-zap" aria-label="Action" class="w-56" />
          <USelect v-model="actor" :items="actorItems" icon="i-lucide-user" aria-label="Actor" class="w-36" />
          <USelect v-model="targetType" :items="targetTypeItems" icon="i-lucide-target" aria-label="Target" class="w-52" />
          <USelect v-model="sinceWindow" :items="SINCE_ITEMS" icon="i-lucide-clock" aria-label="Since" class="w-44" />
          <UInput v-model="searchInput" icon="i-lucide-search" placeholder="Search details…" class="w-64" />
          <UBadge v-if="targetId" color="primary" variant="subtle" class="gap-1" data-target-filter>
            <span class="font-mono">{{ targetId }}</span>
            <UButton icon="i-lucide-x" size="xs" color="neutral" variant="link" aria-label="Clear target" @click="clearTarget" />
          </UBadge>
          <div class="flex-1" />
          <UButton v-if="hasActiveFilters" icon="i-lucide-x" color="neutral" variant="ghost" label="Clear" @click="resetFilters" />
        </div>
      </UDashboardToolbar>
    </template>

    <template #body>
      <FetchErrorAlert v-if="error" :error="error" title="Failed to load the audit trail" class="mb-6" />
      <p class="text-xs text-dimmed mb-2 tabular-nums">
        {{ total.toLocaleString('en-US') }} {{ total === 1 ? 'action' : 'actions' }}
      </p>
      <AuditEventList :events="entries" :pending="pending" empty-text="No operator action matches these filters." />
      <div v-if="total > serverPageSize" class="flex justify-end mt-4">
        <UPagination
          v-model:page="page"
          :total="total"
          :items-per-page="serverPageSize"
          :sibling-count="1"
          active-color="primary"
          variant="subtle"
          :disabled="pending"
          show-edges
        />
      </div>
    </template>
  </UDashboardPanel>
</template>
