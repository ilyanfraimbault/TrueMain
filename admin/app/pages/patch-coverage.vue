<script setup lang="ts">
// Patch coverage (#1033) — "is the current patch servable?".
//
// Answer first: the page opens on one verdict sentence for the patch the site
// actually serves, then the patches behind it. Every other admin page holds a
// piece of this (/aggregation has champion/patch counts, / charts matches by
// patch, /champions has a patch filter) and none of them says whether the tier
// list currently means anything.
//
// The one rule the whole page follows: a low number is never printed on its own.
// "Not yet aggregated" and "aggregated and genuinely thin" produce the same low
// number and call for opposite reactions, so the verdict — not the count — is
// what the reader sees first, and a fold that predates the patch prints its
// "not measured before" sentence instead of a zero.
import type {
  DetectorStatus,
  PatchCoverageRow,
  PatchFoldCoverage,
  PatchVerdict,
} from '~~/shared/types/ops'
import { formatDateTime, formatNumber, formatTimeAgo } from '~~/shared/utils/format'

const { data, pending, error, refresh } = usePatchCoverage()
const { nameFor, iconFor } = useChampionStatic()

const patches = computed<PatchCoverageRow[]>(() => data.value?.patches ?? [])
const current = computed<PatchCoverageRow | null>(
  () => patches.value.find(patch => patch.isCurrent) ?? null,
)

// Same visual language as the data-quality detectors: one dot, one colour, one
// word. `unknown` stays neutral — it says "not measured", and dressing it as
// either a pass or an alarm would be the dashboard lying.
const STATUS_META: Record<DetectorStatus, { dot: string, text: string, label: string }> = {
  green: { dot: 'bg-success', text: 'text-success', label: 'Servable' },
  amber: { dot: 'bg-warning', text: 'text-warning', label: 'Needs attention' },
  red: { dot: 'bg-error', text: 'text-error', label: 'Not servable' },
  unknown: { dot: 'bg-neutral-400 dark:bg-neutral-500', text: 'text-dimmed', label: 'Not measured' },
}

function statusMeta(status: DetectorStatus) {
  return STATUS_META[status] ?? STATUS_META.unknown
}

// The verdict word an operator reads on the badge. Kept apart from the colour on
// purpose: "thin" and "not aggregated" can share a colour while meaning entirely
// different things, and the badge has to carry the difference.
const VERDICT_LABEL: Record<PatchVerdict, string> = {
  servable: 'Servable',
  thin: 'Thin',
  notAggregated: 'Not aggregated',
  unknown: 'No reading',
}

const BADGE_COLOR: Record<DetectorStatus, 'success' | 'error' | 'warning' | 'neutral'> = {
  green: 'success',
  red: 'error',
  amber: 'warning',
  unknown: 'neutral',
}

function badgeColor(status: DetectorStatus) {
  return BADGE_COLOR[status] ?? 'neutral'
}

// Coverage as a share of the lines that exist at all. Empty rather than "0%" when
// there are no lines: "0% of nothing" reads as a measured failure, and there was
// nothing to measure.
function coverageShare(patch: PatchCoverageRow): string {
  return patch.lines > 0 ? `(${Math.round((patch.linesPastFloor / patch.lines) * 100)}%)` : ''
}

/**
 * A fold's numbers in one string, or the sentence explaining why it has none.
 * Never "0 rows" for a fold that predates the patch — that is the one reading
 * that would send an operator hunting a bug that does not exist.
 */
function foldValue(fold: PatchFoldCoverage): string {
  if (!fold.measured) {
    return 'not measured'
  }
  if (fold.rows === null) {
    return 'unknown'
  }
  return `${formatNumber(fold.rows)} rows · ${formatNumber(fold.champions)} champions`
}

// The daily bars are drawn by hand rather than with a chart component: the
// series is a handful of days per patch, and a full chart would carry axes and a
// legend for what is really a shape — "did this patch fill steadily or stop".
function dayScale(patch: PatchCoverageRow): number {
  return Math.max(1, ...patch.daily.map(day => day.matches))
}
</script>

<template>
  <UDashboardPanel id="patch-coverage">
    <template #header>
      <UDashboardNavbar title="Patch Coverage" icon="i-lucide-layers">
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
    </template>

    <template #body>
      <FetchErrorAlert
        v-if="error"
        :error="error"
        title="Failed to load patch coverage"
        class="mb-6"
      />

      <div v-if="pending && !data" class="space-y-6">
        <USkeleton class="h-28 w-full" />
        <USkeleton class="h-64 w-full" />
      </div>

      <template v-else-if="data">
        <!-- The answer, before any table. -->
        <UCard class="mb-6">
          <div class="flex items-start gap-3">
            <span
              class="mt-1.5 size-2.5 shrink-0 rounded-full"
              :class="statusMeta(data.status).dot"
            />
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-baseline gap-x-2 gap-y-1">
                <p class="text-sm font-medium text-highlighted">
                  {{ data.currentPatch ? `Patch ${data.currentPatch} is what the site serves` : 'No patch is being served' }}
                </p>
                <UBadge
                  size="sm"
                  variant="subtle"
                  :color="badgeColor(data.status)"
                  :label="VERDICT_LABEL[data.verdict]"
                />
              </div>
              <p class="mt-1 text-sm text-muted">
                {{ data.headline }}
              </p>
              <!-- An unmeasured page explains itself without a click: the reason IS
                   the finding, and hiding it leaves a grey dot with no story. -->
              <p v-if="data.unknownReason" class="mt-1 text-xs text-dimmed italic">
                {{ data.unknownReason }}
              </p>
              <p class="mt-2 text-xs text-dimmed">
                {{ data.floorNote }}
              </p>
            </div>
          </div>
        </UCard>

        <!-- Headline coverage numbers for the served patch only: the patches
             behind it each carry their own below. -->
        <div v-if="current" class="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
          <UCard>
            <p class="text-xs text-muted uppercase">
              Lines past the floor
            </p>
            <p
              class="mt-1 text-2xl font-semibold tabular-nums"
              :class="statusMeta(current.status).text"
            >
              {{ formatNumber(current.linesPastFloor) }}
            </p>
            <p class="text-xs text-muted tabular-nums">
              of {{ formatNumber(current.lines) }} with any aggregate
            </p>
          </UCard>
          <UCard>
            <p class="text-xs text-muted uppercase">
              Champions rankable
            </p>
            <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
              {{ formatNumber(current.championsPastFloor) }}
            </p>
            <p class="text-xs text-muted tabular-nums">
              of {{ formatNumber(current.champions) }} aggregated
            </p>
          </UCard>
          <UCard>
            <p class="text-xs text-muted uppercase">
              Matches ingested
            </p>
            <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
              {{ formatNumber(current.matches) }}
            </p>
            <p class="text-xs text-muted tabular-nums">
              {{ formatNumber(current.participants) }} participants
            </p>
          </UCard>
          <UCard>
            <p class="text-xs text-muted uppercase">
              Newest game
            </p>
            <p class="mt-1 text-2xl font-semibold text-highlighted tabular-nums">
              {{ formatTimeAgo(current.lastGameStartUtc) }}
            </p>
            <p class="text-xs text-muted tabular-nums">
              {{ formatDateTime(current.lastGameStartUtc) }}
            </p>
          </UCard>
        </div>

        <!-- One card per patch, newest first. -->
        <div class="space-y-6">
          <UCard v-for="patch in patches" :key="patch.patch">
            <template #header>
              <div class="flex flex-wrap items-start justify-between gap-3">
                <div class="flex items-start gap-3 min-w-0">
                  <span
                    class="mt-1.5 size-2 shrink-0 rounded-full"
                    :class="statusMeta(patch.status).dot"
                  />
                  <div class="min-w-0">
                    <div class="flex flex-wrap items-baseline gap-2">
                      <p class="text-sm font-medium text-highlighted">
                        Patch {{ patch.patch }}
                      </p>
                      <UBadge
                        v-if="patch.isCurrent"
                        size="sm"
                        color="primary"
                        variant="subtle"
                        label="served"
                      />
                      <span class="sr-only">— {{ statusMeta(patch.status).label }}</span>
                    </div>
                    <p class="mt-0.5 text-xs text-muted">
                      {{ patch.headline }}
                    </p>
                  </div>
                </div>
                <UBadge
                  variant="subtle"
                  :color="badgeColor(patch.status)"
                  :label="VERDICT_LABEL[patch.verdict]"
                />
              </div>
            </template>

            <dl class="grid grid-cols-2 lg:grid-cols-4 gap-4 mb-4">
              <div>
                <dt class="text-xs text-muted uppercase">
                  Lines past floor
                </dt>
                <dd class="mt-0.5 text-lg font-semibold text-highlighted tabular-nums">
                  {{ formatNumber(patch.linesPastFloor) }} / {{ formatNumber(patch.lines) }}
                  <span class="text-xs font-normal text-muted">{{ coverageShare(patch) }}</span>
                </dd>
              </div>
              <div>
                <dt class="text-xs text-muted uppercase">
                  Champions
                </dt>
                <dd class="mt-0.5 text-lg font-semibold text-highlighted tabular-nums">
                  {{ formatNumber(patch.championsPastFloor) }} / {{ formatNumber(patch.champions) }}
                </dd>
              </div>
              <div>
                <dt class="text-xs text-muted uppercase">
                  Matches
                </dt>
                <dd class="mt-0.5 text-lg font-semibold text-highlighted tabular-nums">
                  {{ formatNumber(patch.matches) }}
                </dd>
              </div>
              <div>
                <dt class="text-xs text-muted uppercase">
                  Participants
                </dt>
                <dd class="mt-0.5 text-lg font-semibold text-highlighted tabular-nums">
                  {{ formatNumber(patch.participants) }}
                </dd>
              </div>
            </dl>

            <p v-if="patch.servableLinesBarNote" class="mb-4 text-xs text-dimmed">
              Bar: {{ patch.servableLinesBarNote }}
            </p>

            <!-- How the patch filled. A patch that stopped ingesting mid-way and
                 one that is still climbing look identical in a total. -->
            <template v-if="patch.daily.length">
              <USeparator class="my-4" />
              <p class="mb-2 text-xs text-muted uppercase">
                Matches by game date
              </p>
              <ul class="space-y-1">
                <li
                  v-for="day in patch.daily"
                  :key="day.date"
                  class="flex items-center gap-3 text-xs"
                >
                  <span class="w-24 shrink-0 tabular-nums text-muted">{{ day.date }}</span>
                  <span class="flex-1 h-2 rounded-full bg-elevated overflow-hidden">
                    <span
                      class="block h-full rounded-full bg-primary"
                      :style="{ width: `${(day.matches / dayScale(patch)) * 100}%` }"
                    />
                  </span>
                  <span class="w-32 shrink-0 text-right tabular-nums text-highlighted">
                    {{ formatNumber(day.matches) }} · {{ formatNumber(day.participants) }} p
                  </span>
                </li>
              </ul>
            </template>

            <!-- Per-fold coverage and freshness. -->
            <USeparator class="my-4" />
            <p class="mb-2 text-xs text-muted uppercase">
              Folds on this patch
            </p>
            <ul class="space-y-2">
              <li
                v-for="fold in patch.folds"
                :key="fold.key"
                class="flex items-start justify-between gap-3"
              >
                <span class="min-w-0">
                  <span class="flex items-center gap-2">
                    <span
                      class="size-1.5 shrink-0 rounded-full"
                      :class="statusMeta(fold.status).dot"
                    />
                    <span class="text-xs font-medium text-highlighted">{{ fold.label }}</span>
                  </span>
                  <!-- The absence sentence replaces the numbers; it never sits
                       beside a zero that would contradict it. -->
                  <span
                    v-if="fold.notMeasuredNote"
                    class="mt-0.5 block ps-3.5 text-xs text-dimmed italic"
                  >{{ fold.notMeasuredNote }}</span>
                  <span
                    v-else-if="fold.note"
                    class="mt-0.5 block ps-3.5 text-xs text-dimmed"
                  >{{ fold.note }}</span>
                </span>
                <span class="shrink-0 text-right">
                  <span
                    class="block text-xs whitespace-nowrap tabular-nums"
                    :class="fold.measured ? 'text-highlighted' : 'text-dimmed'"
                  >{{ foldValue(fold) }}</span>
                  <span class="block text-xs text-muted tabular-nums">
                    <template v-if="fold.measured && fold.lastAggregatedAtUtc">
                      {{ formatTimeAgo(fold.lastAggregatedAtUtc) }}
                    </template>
                    <template v-if="fold.pendingMatches">
                      · {{ formatNumber(fold.pendingMatches) }} to fold
                    </template>
                  </span>
                </span>
              </li>
            </ul>

            <!-- The named cause of a thin patch. -->
            <template v-if="patch.belowFloor.length">
              <USeparator class="my-4" />
              <p class="mb-2 text-xs text-muted uppercase">
                Still below the floor
                <span class="normal-case text-dimmed">
                  — {{ formatNumber(patch.belowFloorCount) }} line(s), closest first<template
                    v-if="patch.belowFloorCount > patch.belowFloor.length"
                  >, showing {{ patch.belowFloor.length }}</template>
                </span>
              </p>
              <ul class="grid gap-1.5 sm:grid-cols-2">
                <li
                  v-for="line in patch.belowFloor"
                  :key="`${line.championId}-${line.position}`"
                  class="flex items-center justify-between gap-2 text-xs"
                >
                  <span class="flex min-w-0 items-center gap-2">
                    <img
                      v-if="iconFor(line.championId)"
                      :src="iconFor(line.championId)!"
                      :alt="nameFor(line.championId)"
                      class="size-5 shrink-0 rounded"
                      loading="lazy"
                    >
                    <span class="truncate text-highlighted">{{ nameFor(line.championId) }}</span>
                    <span class="shrink-0 text-dimmed">{{ line.position }}</span>
                  </span>
                  <span class="shrink-0 tabular-nums text-muted">
                    {{ line.games }} · {{ line.gamesToFloor }} short
                  </span>
                </li>
              </ul>
            </template>
          </UCard>
        </div>

        <p class="mt-6 text-xs text-dimmed">
          {{ data.sourceNote }} Evaluated {{ formatDateTime(data.evaluatedAtUtc) }}.
        </p>
      </template>
    </template>
  </UDashboardPanel>
</template>
