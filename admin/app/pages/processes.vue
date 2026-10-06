<script setup lang="ts">
// Processes panel — background-job run health from `GET /api/ops/process-runs`.
// A per-process rollup (last status / last run / last success / recent failures)
// plus a filterable, server-paginated runs table. The endpoint paginates, so we
// only ever hold one page in memory and drive UPagination off the response's
// `total`/`page`/`pageSize` (the rollup covers the full filtered set, not the
// page). Failed runs are visually distinct (error tint) and each run's
// `summary` JSON + error is inspectable in a slide-over.
//
// Two tabs since #1410, deep-linkable via `?view=runs|riot-api`: the Riot API
// usage panel moved here from a page of its own, because how the pipeline spends
// its call budget is a signal about these same runs, not a separate destination.
import type { ProcessIteration, ProcessRun, ProcessRunStatus } from '~~/shared/types/ops'
// The chain is grouped by lane, not flat: since #1362 an iteration belongs to one
// lane, and drawing the canonical 20 steps every time painted the other lane's
// dozen as phantom "Not run" chips. `pickCurrentLanes` owns that grouping; the
// cards themselves live in `components/processes/` (#1436).
import { pickCurrentLanes } from '~~/shared/utils/pipeline-lanes'

// --- Tabs (#1410) ------------------------------------------------------------
// `runs` is the default and stays out of the URL; `/riot-api` redirects here with
// `?view=riot-api`.
const route = useRoute()
const router = useRouter()
const view = ref<'runs' | 'riot-api'>(route.query.view === 'riot-api' ? 'riot-api' : 'runs')
watch(view, (value) => {
  router.replace({
    query: { ...route.query, view: value === 'riot-api' ? 'riot-api' : undefined },
  })
})
// The ⌘K palette (#1415) links straight to the Riot API tab, and can do so while
// /processes is already open — the component is reused, so setup doesn't re-run
// and only the query changes.
watch(() => route.query.view, (raw) => {
  view.value = raw === 'riot-api' ? 'riot-api' : 'runs'
})

// The Riot API tab owns its own fetch and filters, and exposes `refresh`/`pending`
// so the single navbar button below drives whichever tab is open.
interface RiotApiPanel { refresh: () => void, pending: boolean }
const riotPanel = ref<RiotApiPanel | null>(null)

// --- Filters -----------------------------------------------------------------
const processName = ref('')
const status = ref<'all' | ProcessRunStatus>(ALL)
// Relative window -> ISO `since`. "All" omits the param.
const sinceWindow = ref<SinceWindow>(ALL)

const statusItems = [
  { label: 'All statuses', value: ALL },
  { label: 'Running', value: 'Running' },
  { label: 'Success', value: 'Success' },
  { label: 'Failed', value: 'Failed' },
  { label: 'Abandoned', value: 'Abandoned' },
  { label: 'Cancelled', value: 'Cancelled' },
  { label: 'Skipped', value: 'Skipped' },
]

// Default view = the most recent runs regardless of recency: send NO `since`
// lower bound. The backend applies no time floor unless `since` is explicitly
// provided, so older-but-real runs still show up instead of a misleading
// "0 runs" when nothing ran recently. The time-window select is purely an
// OPTIONAL filter: "All time" omits `since`; the relative windows send their
// computed `since`.
const page = ref(1)
const pageSize = 50

const filters = computed(() => ({
  processName: processName.value.trim() || undefined,
  status: status.value === ALL ? undefined : status.value,
  since: sinceWindow.value === ALL
    ? undefined
    : sinceToIso(sinceWindow.value),
  page: page.value,
  pageSize,
}))

const hasActiveFilters = computed(() =>
  Boolean(
    processName.value.trim()
    || status.value !== ALL
    || sinceWindow.value !== ALL,
  ),
)
function resetFilters() {
  processName.value = ''
  status.value = ALL
  sinceWindow.value = ALL
}

const { data, pending, error, refresh } = useProcessRuns(filters)

// The rollup gets its OWN request (#1411) even though the paged response above
// already carries one. The rollup is live-refreshed and the runs table is not —
// re-ordering a page under a reader's cursor is worse than showing it a page that
// is thirty seconds old — and one fetch cannot be half-live. `pageSize: 1` keeps
// the row payload to the minimum the endpoint will return; only `rollup` is read
// from it. `status` is left out on purpose: the backend computes the rollup from
// the process name and the window only, so sending it would refetch on a filter
// that cannot change the answer.
const rollupFilters = computed(() => ({
  processName: processName.value.trim() || undefined,
  since: sinceWindow.value === ALL ? undefined : sinceToIso(sinceWindow.value),
  page: 1,
  pageSize: 1,
}))
const {
  data: rollupData,
  pending: rollupPending,
  refresh: refreshRollup,
} = useProcessRuns(rollupFilters)

const rollup = computed(() => rollupData.value?.rollup ?? [])
const runs = computed(() => data.value?.runs ?? [])
const total = computed(() => data.value?.total ?? 0)
// The page the server actually served (its clamp wins over our optimistic ref).
const serverPage = computed(() => data.value?.page ?? page.value)
const serverPageSize = computed(() => data.value?.pageSize ?? pageSize)

// Any filter change must reset to the first page — otherwise a narrower filter
// could leave us stranded on a now-out-of-range page.
watch([processName, status, sinceWindow], () => {
  page.value = 1
})

// --- Pipeline chain + iterations ---------------------------------------------
// Recent iterations (one full pass of the chain each), newest first, with their
// per-process runs. Paged independently of the runs table below.
const iterationsPage = ref(1)
const iterationsPageSize = 8
// `finishedOnly` makes the API exclude the in-flight pass from BOTH the page and
// the total, so this list is purely completed history and its pagination stays
// consistent (the running pass is shown by the pipeline chain above, fetched
// separately). Filtering client-side instead would desync `total` from the rows.
const iterationsFilters = computed(() => ({
  page: iterationsPage.value,
  pageSize: iterationsPageSize,
  finishedOnly: true,
}))
const {
  data: iterationsData,
  pending: iterationsPending,
  error: iterationsError,
} = useProcessIterations(iterationsFilters)

const finishedIterations = computed<ProcessIteration[]>(() => iterationsData.value?.iterations ?? [])
const iterationsTotal = computed(() => iterationsData.value?.total ?? 0)
const iterationsServerPage = computed(() => iterationsData.value?.page ?? iterationsPage.value)

// The top "current" block must always reflect the most-recent iteration OF EACH
// LANE, independent of which page of the iterations LIST the operator is viewing.
// A dedicated page-1 fetch keeps it both correct AND live — paginating the list
// below never moves it.
//
// It asks for several iterations rather than one because the lanes run at their
// own cadences: on preprod the fetch lane completes roughly three passes per
// aggregate pass, so the aggregate lane's newest iteration sits a few positions
// down a newest-first list. Six covers that ratio with margin and is still one
// small request; a lane that has not run within the window simply shows as
// not-yet-run rather than being invented.
const LATEST_ITERATION_PROBE = 6
const {
  data: latestIterationData,
  error: latestIterationError,
  refresh: refreshLatestIterations,
} = useProcessIterations({
  page: 1,
  pageSize: LATEST_ITERATION_PROBE,
})
const latestIterations = computed<ProcessIteration[]>(
  () => latestIterationData.value?.iterations ?? [],
)

// The live tree at the top: each lane shown at its own newest iteration. On a
// two-lane deployment those are two different passes running concurrently; on a
// `Full` one, a single iteration supplies both branches. Driven by the dedicated
// latest-iterations fetch so list pagination never changes it. The assembly lives
// in the shared util, with the lanes themselves, so it is pinned by tests.
const currentLanes = computed(() => pickCurrentLanes(latestIterations.value))
const currentIterationRunning = computed(() => currentLanes.value.some(lane => lane.isRunning))

// --- Live refresh (#1411) ----------------------------------------------------
// The two blocks that claim to describe *now* — the lanes in flight and the
// per-process rollup — re-fetch every 30 s while the tab is visible, so a lane
// transition lands without a reload. The runs table and the iterations list are
// deliberately excluded: both are paginated history, and moving rows under the
// operator is the failure mode this page had to avoid.
const {
  lastUpdatedAt,
  paused: livePaused,
  toggle: toggleLive,
  refreshNow,
} = useLiveRefresh([refreshLatestIterations, refreshRollup])

// The navbar button stays the manual, everything-now refresh — the paginated
// table included — and restarts the live countdown.
async function refreshRuns() {
  await Promise.all([refresh(), refreshNow()])
}

// --- Slide-overs -------------------------------------------------------------
// A run (from the chain or the table) and a finished iteration each open their
// own formatted breakdown rather than the raw JSON.
const detailOpen = ref(false)
const selectedRun = ref<ProcessRun | null>(null)
function openDetail(run: ProcessRun) {
  selectedRun.value = run
  detailOpen.value = true
}

const iterationDetailOpen = ref(false)
const selectedIteration = ref<ProcessIteration | null>(null)
function openIterationDetail(iteration: ProcessIteration) {
  selectedIteration.value = iteration
  iterationDetailOpen.value = true
}
</script>

<template>
  <UDashboardPanel id="processes">
    <template #header>
      <UDashboardNavbar title="Processes" icon="i-lucide-activity">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <LiveRefreshIndicator
            v-if="view === 'runs'"
            :last-updated-at="lastUpdatedAt"
            :paused="livePaused"
            @toggle="toggleLive"
          />
          <UButton
            icon="i-lucide-refresh-cw"
            color="neutral"
            variant="ghost"
            :loading="view === 'riot-api' ? (riotPanel?.pending ?? false) : pending"
            aria-label="Refresh"
            @click="view === 'riot-api' ? riotPanel?.refresh() : refreshRuns()"
          />
        </template>
      </UDashboardNavbar>

      <!-- Tab switch: the runs table vs the Riot API usage panel (#1410). -->
      <UDashboardToolbar>
        <template #left>
          <div class="flex items-center gap-1">
            <UButton
              :color="view === 'runs' ? 'primary' : 'neutral'"
              :variant="view === 'runs' ? 'solid' : 'ghost'"
              icon="i-lucide-activity"
              label="Runs"
              @click="void (view = 'runs')"
            />
            <UButton
              :color="view === 'riot-api' ? 'primary' : 'neutral'"
              :variant="view === 'riot-api' ? 'solid' : 'ghost'"
              icon="i-lucide-gauge"
              label="Riot API"
              @click="void (view = 'riot-api')"
            />
          </div>
        </template>
      </UDashboardToolbar>

      <!-- Run filters only: the Riot API tab carries its own, in its body. -->
      <UDashboardToolbar v-if="view === 'runs'">
        <template #left>
          <UInput
            v-model="processName"
            icon="i-lucide-search"
            placeholder="Process name…"
            class="w-56"
          />
          <USelect
            v-model="status"
            :items="statusItems"
            icon="i-lucide-check-circle"
            placeholder="Status"
            class="w-44"
          />
          <USelect
            v-model="sinceWindow"
            :items="SINCE_ITEMS"
            icon="i-lucide-clock"
            placeholder="Since"
            class="w-44"
          />
        </template>
        <template #right>
          <UButton
            v-if="hasActiveFilters"
            icon="i-lucide-x"
            color="neutral"
            variant="ghost"
            label="Clear"
            @click="resetFilters"
          />
        </template>
      </UDashboardToolbar>
    </template>

    <template #body>
      <ProcessesRiotApi v-if="view === 'riot-api'" ref="riotPanel" />

      <template v-else>
        <FetchErrorAlert
          v-if="error"
          :error="error"
          title="Failed to load process runs"
          class="mb-6"
        />

        <ProcessesPipelineChain
          :lanes="currentLanes"
          :running="currentIterationRunning"
          :error="latestIterationError"
          @open-run="openDetail"
        />

        <ProcessesRecentIterations
          v-model:page="iterationsPage"
          :iterations="finishedIterations"
          :pending="iterationsPending"
          :error="iterationsError"
          :total="iterationsTotal"
          :page-size="iterationsPageSize"
          :server-page="iterationsServerPage"
          @open-iteration="openIterationDetail"
        />

        <ProcessesHealthGrid
          :rollup="rollup"
          :pending="rollupPending"
          :all-time="sinceWindow === ALL"
        />

        <ProcessesRunsTable
          v-model:page="page"
          :runs="runs"
          :total="total"
          :pending="pending"
          :server-page="serverPage"
          :server-page-size="serverPageSize"
          :has-active-filters="hasActiveFilters"
          :all-time="sinceWindow === ALL"
          @open-run="openDetail"
        />

        <ProcessesRunDetail v-model:open="detailOpen" :run="selectedRun" />
        <ProcessesIterationDetail v-model:open="iterationDetailOpen" :iteration="selectedIteration" />
      </template>
    </template>
  </UDashboardPanel>
</template>
