<script setup lang="ts">
// Processes panel — background-job run health: a per-process rollup (last status /
// last run / last success / recent failures, `GET /api/ops/process-runs/rollup`) plus a
// filterable, server-paginated runs table (`GET /api/ops/process-runs`), driven off the
// response's `total`/`page`/`pageSize`. Failed runs are visually distinct (error tint) and each run's
// `summary` JSON + error is inspectable in a slide-over.
//
// Two tabs since #1410, deep-linkable via `?view=runs|riot-api`: the Riot API
// usage panel moved here from a page of its own, because how the pipeline spends
// its call budget is a signal about these same runs, not a separate destination.
import type { ProcessIterationReadModel, ProcessRunReadModel, ProcessRunStatus } from '#site-shared/contract/api'
import type { PipelineLaneId } from '~~/shared/types/pipeline-chain'
// The chain is grouped by lane, not flat: since #1362 an iteration belongs to one
// lane, and drawing the canonical 20 steps every time painted the other lane's
// dozen as phantom "Not run" chips. `pickCurrentLanes` owns that grouping; the
// cards themselves live in `components/processes/` (#1436).
import { pickCurrentLanes } from '~~/shared/utils/pipeline-lanes'
import { processRunStatusValues } from '#site-shared/contract/api'

// --- Tabs (#1410) ------------------------------------------------------------
// `runs` is the default and stays out of the URL; `/riot-api` redirects here with
// `?view=riot-api`. `scrapers` (#2119) holds the ops scripts kept outside the repo;
// `performance` (#2307) how fast the ops API answers the admin.
type ProcessesView = 'runs' | 'riot-api' | 'scrapers' | 'performance'
const TAB_VIEWS: ReadonlySet<string> = new Set(['riot-api', 'scrapers', 'performance'])
const route = useRoute()
const router = useRouter()
function parseView(raw: unknown): ProcessesView {
  return typeof raw === 'string' && TAB_VIEWS.has(raw) ? raw as ProcessesView : 'runs'
}
const view = ref<ProcessesView>(parseView(route.query.view))
watch(view, (value) => {
  router.replace({
    query: { ...route.query, view: value === 'runs' ? undefined : value },
  })
})
// The ⌘K palette (#1415) links straight to the Riot API tab, and can do so while
// /processes is already open — the component is reused, so setup doesn't re-run
// and only the query changes.
watch(() => route.query.view, (raw) => {
  view.value = parseView(raw)
})

// The Riot API and Scrapers tabs own their fetches, and expose `refresh`/`pending`
// so the single navbar button below drives whichever tab is open.
interface TabPanel { refresh: () => void, pending: boolean }
const riotPanel = ref<TabPanel | null>(null)
const scrapersPanel = ref<TabPanel | null>(null)
const performancePanel = ref<TabPanel | null>(null)
const activePanel = computed(() => ({
  'runs': null,
  'riot-api': riotPanel.value,
  'scrapers': scrapersPanel.value,
  'performance': performancePanel.value,
})[view.value])

// --- Filters -----------------------------------------------------------------
const processName = ref('')
const status = ref<'all' | ProcessRunStatus>(ALL)
// Relative window -> ISO `since`. "All" omits the param.
const sinceWindow = ref<SinceWindow>(ALL)

const statusItems = [
  { label: 'All statuses', value: ALL },
  ...processRunStatusValues.map(status => ({ label: status, value: status })),
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

// The rollup is live-refreshed and the runs table is not (#1411): re-ordering a page
// under a reader's cursor is worse than showing it a page that is thirty seconds old.
// `status` is not a rollup filter: the rollup covers the process and the window only.
const rollupFilters = computed(() => ({
  processName: processName.value.trim() || undefined,
  since: sinceWindow.value === ALL ? undefined : sinceToIso(sinceWindow.value),
}))
const {
  data: rollupData,
  pending: rollupPending,
  refresh: refreshRollup,
} = useProcessRollup(rollupFilters)

const rollup = computed(() => rollupData.value ?? [])
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
// Filtered server-side so the pagination counts the lane's passes only.
const iterationsLane = ref<'all' | PipelineLaneId>('all')
watch(iterationsLane, () => {
  iterationsPage.value = 1
})
// `finishedOnly` makes the API exclude the in-flight pass from BOTH the page and
// the total, so this list is purely completed history and its pagination stays
// consistent (the running pass is shown by the pipeline chain above, fetched
// separately). Filtering client-side instead would desync `total` from the rows.
const iterationsFilters = computed(() => ({
  page: iterationsPage.value,
  pageSize: iterationsPageSize,
  finishedOnly: true,
  lane: iterationsLane.value === 'all' ? undefined : iterationsLane.value,
}))
const {
  data: iterationsData,
  pending: iterationsPending,
  error: iterationsError,
} = useProcessIterations(iterationsFilters)

const finishedIterations = computed<ProcessIterationReadModel[]>(() => iterationsData.value?.iterations ?? [])
const iterationsTotal = computed(() => iterationsData.value?.total ?? 0)
const iterationsServerPage = computed(() => iterationsData.value?.page ?? iterationsPage.value)

// The top "current" block shows the newest pass OF EACH LANE, whichever page of the
// iterations list is open below: its own read, so paginating never moves it.
const {
  data: latestIterationData,
  error: latestIterationError,
  refresh: refreshLatestIterations,
} = useLatestProcessIterations()
const latestIterations = computed<ProcessIterationReadModel[]>(() => latestIterationData.value ?? [])

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
// own formatted breakdown rather than the raw JSON. The run is read back from the
// live feeds (#2471), so a Running one settles in place when the chain refreshes.
const selected = ref<ProcessRunReadModel | null>(null)
const detailOpen = ref(false)
const selectedRun = computed(() => liveRun(selected.value, [
  ...latestIterations.value.map(iteration => iteration.runs),
  runs.value,
  ...finishedIterations.value.map(iteration => iteration.runs),
]))
function openDetail(run: ProcessRunReadModel) {
  selected.value = run
  detailOpen.value = true
}

const iterationDetailOpen = ref(false)
const selectedIteration = ref<ProcessIterationReadModel | null>(null)
function openIterationDetail(iteration: ProcessIterationReadModel) {
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
            :loading="view === 'runs' ? pending : (activePanel?.pending ?? false)"
            aria-label="Refresh"
            @click="view === 'runs' ? refreshRuns() : activePanel?.refresh()"
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
            <UButton
              :color="view === 'scrapers' ? 'primary' : 'neutral'"
              :variant="view === 'scrapers' ? 'solid' : 'ghost'"
              icon="i-lucide-bot"
              label="Scrapers"
              @click="void (view = 'scrapers')"
            />
            <UButton
              :color="view === 'performance' ? 'primary' : 'neutral'"
              :variant="view === 'performance' ? 'solid' : 'ghost'"
              icon="i-lucide-timer"
              label="Performance"
              @click="void (view = 'performance')"
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
      <ProcessesScrapers v-else-if="view === 'scrapers'" ref="scrapersPanel" />
      <ProcessesPerformance v-else-if="view === 'performance'" ref="performancePanel" />

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
          v-model:lane="iterationsLane"
          :iterations="finishedIterations"
          :pending="iterationsPending"
          :error="iterationsError"
          :total="iterationsTotal"
          :page-size="iterationsPageSize"
          :server-page="iterationsServerPage"
          @open-iteration="openIterationDetail"
          @open-run="openDetail"
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
