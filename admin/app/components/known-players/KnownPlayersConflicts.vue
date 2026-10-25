<script setup lang="ts">
// The known-players conflict queue (#2253): accounts a scraper lists for one known
// player while another already holds them. The intake never moves them on its own;
// the operator reads both players side by side and settles it here. The decision
// holds against the next scraper runs (an exclusion, or a merged page's pointer).
import type { PersonaConflict, PersonaConflictDetail, PersonaConflictResolveBody, PersonaConflictStatus } from '~~/shared/types/ops'
import { computed, ref, watch } from 'vue'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'
import { resolutionLabel, resolvedMessage, resolveErrorMessage, riotId } from '~~/shared/utils/persona-conflicts'
import { autoMergedMessage } from '~~/shared/utils/persona-homonyms'

const SOURCES = ['Leaguepedia', 'TrackingThePros', 'DeepLol', 'Dpm', 'LoLPros', 'Manual']
const sourceItems = [{ label: 'All sources', value: ALL }, ...SOURCES.map(s => ({ label: s, value: s }))]

const status = defineModel<PersonaConflictStatus>('status', { required: true })
const sourceFilter = ref<string>(ALL)
const searchFilter = ref('')
const searchDebounced = refDebounced(searchFilter, 300)
const page = ref(1)
const pageSize = 25

watch([status, sourceFilter, searchDebounced], () => {
  page.value = 1
})

const filters = computed(() => ({
  status: status.value,
  source: sourceFilter.value === ALL ? undefined : sourceFilter.value,
  search: searchDebounced.value.trim() || undefined,
  page: page.value,
  pageSize,
}))

const { data, pending, error, refresh } = usePersonaConflicts(filters)
const conflicts = computed(() => data.value?.conflicts ?? [])
const total = computed(() => data.value?.total ?? 0)
const hasFilters = computed(() => sourceFilter.value !== ALL || Boolean(searchFilter.value.trim()))
const emptyText = computed(() => hasFilters.value
  ? 'No conflict matches these filters.'
  : status.value === 'Open' ? 'No open conflict: every account has one known player.' : 'Nothing resolved yet.')

// The detail panel: both known players in full, then the actions.
const detailOpen = ref(false)
const detail = ref<PersonaConflictDetail | null>(null)
const detailError = ref<unknown>(null)
const busy = ref(false)
const toast = useActionToast()

async function open(conflict: Pick<PersonaConflict, 'id'>) {
  detail.value = null
  detailError.value = null
  detailOpen.value = true
  try {
    detail.value = await getPersonaConflict(conflict.id)
  }
  catch (e) {
    detailError.value = e
  }
}

// `?conflict=<id>` opens one conflict: the known player's edit panel links its open
// conflicts here (#2019). Closing the panel drops it, so a reload does not reopen it.
const route = useRoute()
const router = useRouter()
watch(() => route.query.conflict, (id) => {
  if (typeof id === 'string' && id && detail.value?.conflict.id !== id) {
    open({ id })
  }
}, { immediate: true })
watch(detailOpen, (isOpen) => {
  if (!isOpen && route.query.conflict) {
    router.replace({ query: { ...route.query, conflict: undefined } })
  }
})

async function resolve(body: PersonaConflictResolveBody) {
  if (!detail.value) {
    return
  }

  busy.value = true
  try {
    const resolved = await resolvePersonaConflict(detail.value.conflict.id, body)
    const message = resolvedMessage(resolved)
    toast.success(message.title, message.description)
    detailOpen.value = false
  }
  catch (e) {
    toast.failure('Conflict not resolved', resolveErrorMessage(e))
  }
  finally {
    busy.value = false
    refresh()
  }
}

// Two known players with the same name claiming one account are one person (#2271):
// the intake merges them after each batch; this clears what was queued before.
const autoMerging = ref(false)
async function autoMerge() {
  autoMerging.value = true
  try {
    const message = autoMergedMessage(await autoMergePersonaConflicts())
    toast.success(message.title, message.description)
  }
  catch (e) {
    toast.failure('Same-name conflicts not merged', resolveErrorMessage(e))
  }
  finally {
    autoMerging.value = false
    refresh()
  }
}

defineExpose({ refresh, pending, counts: computed(() => ({ open: data.value?.open ?? 0, resolved: data.value?.resolved ?? 0 })) })
</script>

<template>
  <section>
    <div class="flex flex-wrap items-center gap-2 mb-3">
      <UInput v-model="searchFilter" icon="i-lucide-search" placeholder="Player or Riot ID" class="w-full sm:w-64" :loading="pending" />
      <USelect v-model="sourceFilter" :items="sourceItems" icon="i-lucide-database" class="w-48" />
      <UButton v-if="hasFilters" icon="i-lucide-x" color="neutral" variant="ghost" label="Clear" @click="sourceFilter = ALL; searchFilter = ''" />
      <UButton
        v-if="status === 'Open'"
        data-action="auto-merge"
        icon="i-lucide-merge"
        color="primary"
        variant="subtle"
        label="Merge same-name conflicts"
        class="sm:ml-auto"
        :loading="autoMerging"
        :disabled="autoMerging || busy"
        @click="autoMerge()"
      />
    </div>

    <FetchErrorAlert v-if="error" :error="error" title="Failed to load the conflicts" class="mb-4" />

    <UCard :ui="{ body: 'p-0 sm:p-0' }">
      <template #header>
        <div class="flex items-center justify-between gap-2">
          <p class="text-sm font-medium text-highlighted">
            {{ status === 'Open' ? 'To settle' : 'History' }}
          </p>
          <UBadge v-if="!pending" color="neutral" variant="subtle" :label="`${formatNumber(total)} ${total === 1 ? 'conflict' : 'conflicts'}`" />
        </div>
      </template>

      <KnownPlayersConflictList :conflicts="conflicts" :pending="pending" :empty-text="emptyText" @open="open" />

      <div v-if="total > pageSize" class="flex justify-end border-t border-default px-4 py-3">
        <UPagination v-model:page="page" :total="total" :items-per-page="pageSize" :sibling-count="1" active-color="primary" variant="subtle" :disabled="pending" show-edges />
      </div>
    </UCard>

    <USlideover
      v-model:open="detailOpen"
      :title="detail ? riotId(detail.conflict.account) : 'Conflict'"
      :description="detail ? `${detail.conflict.account.platformId} · claimed ${formatNumber(detail.conflict.timesSeen)}× since ${formatDateTime(detail.conflict.firstSeenAtUtc)}` : ''"
      :ui="{ content: 'sm:max-w-3xl' }"
    >
      <template #body>
        <FetchErrorAlert v-if="detailError" :error="detailError" title="Failed to load this conflict" />
        <div v-else-if="detail" class="space-y-6">
          <div class="grid gap-6 sm:grid-cols-2">
            <KnownPlayersPersonaCard :persona="detail.claimant" side="Claims it" />
            <KnownPlayersPersonaCard :persona="detail.owner" side="Holds it" />
          </div>
          <USeparator />
          <KnownPlayersConflictActions
            v-if="detail.conflict.status === 'Open'"
            :conflict="detail.conflict"
            :busy="busy"
            @resolve="resolve"
          />
          <p v-else class="text-sm text-muted">
            {{ resolutionLabel(detail.conflict.resolution) }} on {{ formatDateTime(detail.conflict.resolvedAtUtc) }}.
          </p>
        </div>
        <div v-else class="py-10 text-center">
          <UIcon name="i-lucide-loader-circle" class="size-5 animate-spin text-muted" />
        </div>
      </template>
    </USlideover>
  </section>
</template>
