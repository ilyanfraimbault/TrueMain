<script setup lang="ts">
// Health cockpit (#1031) — the 30-second glance, backed by `GET /api/ops/pipeline-health`.
//
// Answering "is the pipeline healthy right now?" used to mean opening four pages: `/` for
// throughput, `/processes` for per-process status, `/data-quality` for the detector verdict
// and `/database` for the disk forecast. Each of those is right for its own depth; none is
// the glance. This page is the glance and *only* the glance — every tile links to the page
// that owns its detail, and nothing here is a measurement this page invented.
//
// The verdict is computed server-side, not here: thresholds are a domain decision, and a
// tile that judged a signal differently from the panel it links to would be lying.
import { detectorStatusMeta } from '~~/shared/utils/detector-status'
import { formatDateTime, formatDuration, formatNumber, formatTimeAgo } from '~~/shared/utils/format'
import {
  championDataLagLabel,
  ingestionToAnalysisLabel,
  processStatusColor,
} from '~~/shared/utils/pipeline-health'

const { data, pending, error, refresh } = usePipelineHealth()

const verdict = computed(() => detectorStatusMeta(data.value?.status))
const signals = computed(() => data.value?.signals ?? [])
const processes = computed(() => data.value?.processes ?? [])
const rawData = computed(() => data.value?.rawData ?? null)
const gaps = computed(() => data.value?.gaps ?? null)

// Processes worth reading without a click: anything whose latest run did not succeed. A
// healthy pipeline collapses to a single line, so the section reads as a list of problems
// rather than ten rows of green.
const showAllProcesses = ref(false)
const troubledProcesses = computed(() =>
  processes.value.filter(process => process.status !== 'Success'),
)
const visibleProcesses = computed(() =>
  showAllProcesses.value ? processes.value : troubledProcesses.value,
)
</script>

<template>
  <UDashboardPanel id="health">
    <template #header>
      <UDashboardNavbar title="Health" icon="i-lucide-heart-pulse">
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
        title="Failed to load pipeline health"
        class="mb-6"
      />

      <!-- The verdict, first and alone. One sentence, its evaluation time stated: a
           cockpit that hides how old it is gets read as live. -->
      <USkeleton v-if="pending && !data" class="h-24 w-full mb-6" />
      <UCard v-else-if="data" class="mb-6">
        <div class="flex items-start gap-4">
          <UIcon :name="verdict.icon" class="size-8 shrink-0" :class="verdict.text" />
          <div class="min-w-0">
            <p class="text-xl font-semibold text-highlighted">
              {{ data.headline }}
            </p>
            <p class="mt-1 text-xs text-muted">
              {{ verdict.label }} · evaluated {{ formatTimeAgo(data.evaluatedAtUtc) }}
              <span class="text-dimmed">({{ formatDateTime(data.evaluatedAtUtc) }})</span>
            </p>
          </div>
        </div>
      </UCard>

      <!-- Signal tiles, worst first. Each one is a link; this page adds no detail. -->
      <div v-if="pending && !data" class="grid gap-4 sm:grid-cols-2 mb-6">
        <USkeleton v-for="index in 4" :key="index" class="h-28 w-full" />
      </div>
      <div v-else-if="signals.length > 0" class="grid gap-4 sm:grid-cols-2 mb-8">
        <NuxtLink
          v-for="signal in signals"
          :key="signal.key"
          :to="signal.detailPath"
          class="group rounded-lg focus-visible:outline-2 focus-visible:outline-primary"
        >
          <UCard
            class="h-full transition-colors group-hover:bg-elevated/50"
            :ui="{ body: 'h-full' }"
          >
            <div class="flex items-start gap-3">
              <span
                class="mt-1.5 size-2 shrink-0 rounded-full"
                :class="detectorStatusMeta(signal.status).dot"
                :aria-label="detectorStatusMeta(signal.status).label"
              />
              <div class="min-w-0 flex-1">
                <div class="flex items-center justify-between gap-2">
                  <p class="text-sm font-medium text-highlighted">
                    {{ signal.title }}
                  </p>
                  <UIcon
                    name="i-lucide-arrow-up-right"
                    class="size-4 shrink-0 text-dimmed group-hover:text-muted"
                  />
                </div>
                <!-- An unmeasured signal states why, in place. A zero here would read as
                     a pass, which is the failure mode this page exists to prevent. -->
                <p
                  class="mt-1 text-sm"
                  :class="signal.unknownReason ? 'text-dimmed italic' : 'text-muted'"
                >
                  {{ signal.unknownReason ?? signal.headline }}
                </p>
                <p class="mt-2 text-xs text-dimmed">
                  {{ signal.detailPath }}
                </p>
              </div>
            </div>
          </UCard>
        </NuxtLink>
      </div>

      <!-- Per-process rollup: last run, last success, current failure streak. -->
      <section v-if="data" class="mb-8">
        <div class="flex items-center justify-between gap-2 mb-3">
          <h2 class="text-sm font-semibold text-highlighted uppercase">
            Processes
          </h2>
          <div class="flex items-center gap-2">
            <UButton
              v-if="troubledProcesses.length > 0 && !showAllProcesses"
              size="xs"
              color="neutral"
              variant="ghost"
              :label="`Show all ${processes.length}`"
              @click="(showAllProcesses = true, undefined)"
            />
            <UButton
              v-else-if="showAllProcesses"
              size="xs"
              color="neutral"
              variant="ghost"
              label="Show only problems"
              @click="(showAllProcesses = false, undefined)"
            />
            <UButton
              to="/processes"
              size="xs"
              color="neutral"
              variant="subtle"
              trailing-icon="i-lucide-arrow-up-right"
              label="Runs & iterations"
            />
          </div>
        </div>

        <p
          v-if="troubledProcesses.length === 0 && !showAllProcesses"
          class="text-sm text-muted"
        >
          All {{ processes.length }} processes last ran without failing.
          <UButton
            size="xs"
            color="neutral"
            variant="link"
            label="Show them"
            @click="(showAllProcesses = true, undefined)"
          />
        </p>

        <ul v-else class="divide-y divide-default rounded-lg ring ring-default">
          <li
            v-for="process in visibleProcesses"
            :key="process.processName"
            class="flex flex-wrap items-center gap-x-4 gap-y-1 px-4 py-2.5"
          >
            <UBadge
              :color="processStatusColor(process.status)"
              variant="subtle"
              :label="process.status"
              class="shrink-0"
            />
            <span class="text-sm text-highlighted font-medium grow min-w-0 truncate">
              {{ process.processName }}
            </span>
            <span class="text-xs text-muted tabular-nums">
              last run {{ process.lastStartedAtUtc ? formatTimeAgo(process.lastStartedAtUtc) : 'never' }}
            </span>
            <span class="text-xs tabular-nums" :class="process.lastSuccessAtUtc ? 'text-muted' : 'text-dimmed italic'">
              <!-- "Never succeeded" and "succeeded long ago" are different answers and must
                   not collapse into one dash. -->
              {{ process.lastSuccessAtUtc
                ? `last success ${formatTimeAgo(process.lastSuccessAtUtc)}`
                : 'never succeeded' }}
            </span>
            <span
              v-if="process.consecutiveFailures > 0"
              class="text-xs text-error tabular-nums"
            >
              {{ process.consecutiveFailures }} consecutive
              {{ process.consecutiveFailures === 1 ? 'failure' : 'failures' }}
            </span>
            <span v-if="process.durationMs > 0" class="text-xs text-dimmed tabular-nums">
              {{ formatDuration(process.durationMs) }}
            </span>
          </li>
        </ul>
      </section>

      <!-- The raw measurements the signals were judged from, stated without a verdict. -->
      <section v-if="rawData || gaps" class="grid gap-4 lg:grid-cols-2">
        <UCard v-if="rawData">
          <template #header>
            <div class="flex items-center justify-between gap-2">
              <p class="text-sm font-semibold text-highlighted">
                Raw corpus
              </p>
              <span class="text-xs text-muted tabular-nums">queue {{ rawData.queueId }}</span>
            </div>
          </template>

          <div class="grid grid-cols-2 gap-4 mb-4">
            <div>
              <p class="text-xs text-muted uppercase">
                Matches
              </p>
              <p class="mt-0.5 text-xl font-semibold text-highlighted tabular-nums">
                {{ formatNumber(rawData.rawMatchCount) }}
              </p>
            </div>
            <div>
              <p class="text-xs text-muted uppercase">
                Participants
              </p>
              <p class="mt-0.5 text-xl font-semibold text-highlighted tabular-nums">
                {{ formatNumber(rawData.rawParticipantCount) }}
              </p>
            </div>
          </div>

          <p v-if="rawData.platforms.length === 0" class="text-sm text-dimmed italic">
            No match ingested for this queue yet.
          </p>
          <ul v-else class="space-y-1">
            <li
              v-for="platform in rawData.platforms"
              :key="platform.platformId"
              class="flex items-center justify-between gap-2 text-sm"
            >
              <span class="text-highlighted font-medium">{{ platform.platformId }}</span>
              <span class="text-xs text-muted tabular-nums">
                patch {{ platform.latestPatchVersion || '—' }} ·
                {{ platform.latestMatchStartAtUtc ? formatTimeAgo(platform.latestMatchStartAtUtc) : 'no match' }}
              </span>
            </li>
          </ul>
        </UCard>

        <UCard v-if="gaps">
          <template #header>
            <p class="text-sm font-semibold text-highlighted">
              Pipeline gaps
            </p>
          </template>

          <dl class="space-y-4">
            <div>
              <dt class="text-xs text-muted uppercase">
                Ingestion → analysis
              </dt>
              <dd class="mt-0.5 text-sm text-highlighted">
                {{ ingestionToAnalysisLabel(gaps.matchIngestionToMainAnalysisMinutes) }}
              </dd>
              <p class="text-xs text-dimmed">
                Between the newest successful MatchIngestion and MainAnalysis runs.
              </p>
            </div>
            <div>
              <dt class="text-xs text-muted uppercase">
                Champion data
              </dt>
              <dd class="mt-0.5 text-sm text-highlighted">
                {{ championDataLagLabel(gaps.championDataLagMinutes) }}
              </dd>
              <p class="text-xs text-dimmed">
                Between the newest ingested match and the newest computed champion stats.
              </p>
            </div>
          </dl>
        </UCard>
      </section>
    </template>
  </UDashboardPanel>
</template>
