<script setup lang="ts">
// The known-players conflict queue (#2253): accounts a scraper lists for one known
// player while another already holds them. The intake never moves them on its own;
// the operator reads both players lined up row by row (#2275) and settles it here. The decision
// holds against the next scraper runs (an exclusion, or a merged page's pointer).
// Below the open queue, the known players sharing a social channel (#2286).
// The open queue is worked from the keyboard (#2293, `useConflictTriage`): settling
// one conflict opens the next. Its most certain top is merged in bulk (#2295).
import type { PersonaConflict, PersonaConflictDetail, PersonaConflictResolveBody, PersonaConflictStatus } from '~~/shared/types/ops'
import { useConflictTriage } from '~/composables/useConflictTriage'
import { computed, ref, watch } from 'vue'
import { formatDateTime, formatNumber } from '~~/shared/utils/format'
import { nextAfterResolve } from '~~/shared/utils/conflict-shortcuts'
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

const { data, pending, error, refresh: refreshAccounts } = usePersonaConflicts(filters)
// The channel conflicts below reload when this stamp moves, and report their count for the tab.
const channelStamp = ref(0)
const channelTotal = ref(0)

function refresh() {
  refreshAccounts()
  channelStamp.value++
}
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
const actions = ref<{ choose: (key: string) => void, confirm: () => void, armed: string | null } | null>(null)

async function open(conflict: Pick<PersonaConflict, 'id'>) {
  triage.select(conflict)
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

// The bulk resolution's dialog: the queue's keys stay quiet while it is open.
const bulkOpen = ref(false)

const triage = useConflictTriage({
  rows: conflicts,
  detailOpen,
  enabled: () => status.value === 'Open' && !bulkOpen.value,
  canAct: () => detail.value?.conflict.status === 'Open' && !busy.value,
  armed: () => Boolean(actions.value?.armed),
  open,
  act: key => actions.value?.choose(key),
  confirm: () => actions.value?.confirm(),
})
const helpOpen = triage.helpOpen
const selectedId = computed(() => conflicts.value[triage.selected.value]?.id ?? null)

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

  const id = detail.value.conflict.id
  const index = conflicts.value.findIndex(c => c.id === id)
  busy.value = true
  let settled = false
  try {
    const message = resolvedMessage(await resolvePersonaConflict(id, body))
    toast.success(message.title, message.description)
    settled = true
  }
  catch (e) {
    toast.failure('Conflict not resolved', resolveErrorMessage(e))
  }
  busy.value = false
  channelStamp.value++
  await refreshAccounts()
  if (settled) {
    openNext(id, index < 0 ? triage.selected.value : index)
  }
}

// After a resolution the next conflict opens, so the queue is worked without the mouse.
function openNext(resolvedId: string, index: number) {
  if (route.query.conflict) {
    router.replace({ query: { ...route.query, conflict: undefined } })
  }
  const next = status.value === 'Open' ? nextAfterResolve(conflicts.value, resolvedId, index) : null
  if (next) {
    open(next)
  }
  else {
    detailOpen.value = false
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

defineExpose({
  refresh,
  pending,
  counts: computed(() => ({ open: (data.value?.open ?? 0) + channelTotal.value, resolved: data.value?.resolved ?? 0 })),
})
</script>

<template>
  <section>
    <div class="flex flex-wrap items-center gap-2 mb-3">
      <UInput v-model="searchFilter" icon="i-lucide-search" placeholder="Player or Riot ID" class="w-full sm:w-64" :loading="pending" />
      <USelect v-model="sourceFilter" :items="sourceItems" icon="i-lucide-database" class="w-48" />
      <UButton v-if="hasFilters" icon="i-lucide-x" color="neutral" variant="ghost" label="Clear" @click="sourceFilter = ALL; searchFilter = ''" />
      <UButton
        v-if="status === 'Open'"
        data-action="bulk-resolve"
        icon="i-lucide-list-checks"
        color="primary"
        variant="subtle"
        label="Resolve the certain ones"
        class="sm:ml-auto"
        :disabled="autoMerging || busy"
        @click="bulkOpen = true"
      />
      <UButton
        v-if="status === 'Open'"
        data-action="auto-merge"
        icon="i-lucide-merge"
        color="primary"
        variant="subtle"
        label="Merge same-name conflicts"
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
            {{ status === 'Open' ? 'Accounts to settle' : 'History' }}
          </p>
          <div class="flex items-center gap-2">
            <UButton
              v-if="status === 'Open'"
              data-action="shortcuts"
              icon="i-lucide-keyboard"
              color="neutral"
              variant="ghost"
              size="xs"
              aria-label="Keyboard shortcuts"
              @click="helpOpen = true"
            >
              <template #trailing>
                <UKbd value="?" size="sm" />
              </template>
            </UButton>
            <UBadge v-if="!pending" color="neutral" variant="subtle" :label="`${formatNumber(total)} ${total === 1 ? 'conflict' : 'conflicts'}`" />
          </div>
        </div>
      </template>

      <KnownPlayersConflictList :conflicts="conflicts" :pending="pending" :empty-text="emptyText" :selected-id="selectedId" @open="open" />

      <div v-if="total > pageSize" class="flex justify-end border-t border-default px-4 py-3">
        <UPagination v-model:page="page" :total="total" :items-per-page="pageSize" :sibling-count="1" active-color="primary" variant="subtle" :disabled="pending" show-edges />
      </div>
    </UCard>

    <KnownPlayersChannelConflicts
      v-show="status === 'Open'"
      :search="searchDebounced.trim()"
      :stamp="channelStamp"
      class="mt-4"
      @total="channelTotal = $event"
    />

    <USlideover
      v-model:open="detailOpen"
      :title="detail ? riotId(detail.conflict.account) : 'Conflict'"
      :description="detail ? `${detail.conflict.account.platformId} · claimed ${formatNumber(detail.conflict.timesSeen)}× since ${formatDateTime(detail.conflict.firstSeenAtUtc)}` : ''"
      :ui="{ content: 'sm:max-w-3xl' }"
    >
      <template #body>
        <FetchErrorAlert v-if="detailError" :error="detailError" title="Failed to load this conflict" />
        <div v-else-if="detail" class="space-y-6">
          <KnownPlayersConflictCompare :claimant="detail.claimant" :owner="detail.owner" />
          <USeparator />
          <KnownPlayersConflictActions
            v-if="detail.conflict.status === 'Open'"
            ref="actions"
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

    <KnownPlayersShortcutsHelp v-model:open="helpOpen" />
    <KnownPlayersBulkResolve v-if="status === 'Open'" v-model:open="bulkOpen" @resolved="refresh()" />
  </section>
</template>
