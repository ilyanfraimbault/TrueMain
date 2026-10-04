<script setup lang="ts">
// Region balance (#1153) — "are the regions balanced?" from one panel, without reading a
// `MatchIngestion` run summary or a claim log line.
//
// Informational, like the Riot API tile: it feeds no signal and does not move the verdict.
// Every figure is the backend's; the only thing computed here is the chart pivot, and the
// zero days it fills are measured (the query covers the whole window).
import type { RegionBalance } from '~~/shared/types/ops'
import { formatDateTime, formatNumber, formatPercentOrDash, formatTimeAgo } from '~~/shared/utils/format'
import { chartedPlatforms, claimLabel, regionDailyRows } from '~~/shared/utils/region-balance'
import { CHART_SERIES } from '~/utils/chart-palette'
import { formatCount, indexLabelFormatter, labelTooltipTitle, multiTimeBarProps } from '~/utils/charts'

const props = defineProps<{ balance: RegionBalance }>()

const platforms = computed(() => chartedPlatforms(props.balance))
const chartData = computed(() => regionDailyRows(props.balance, platforms.value))
const chartCategories = computed(() =>
  Object.fromEntries(platforms.value.map((platform, index) => [
    platform,
    { name: platform, color: CHART_SERIES[index % CHART_SERIES.length] },
  ])),
)
const xFormatter = computed(() => indexLabelFormatter(chartData.value, row => row.label))
const coverageKnown = computed(() => props.balance.targetMainsPerChampion !== null)
</script>

<template>
  <UCard>
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-2">
        <PanelTitle
          title="Region balance"
          :subtitle="`Informational, not part of the verdict · last ${balance.windowDays} days of ingestion`"
        >
          <template #info>
            <p>
              <em>Coverage deficit</em> is how far a region is from
              <code>Coverage:TargetMainsPerChampion</code> active mains per champion,
              averaged over every champion that has an active main in any region —
              0% means every champion is at target there, 100% means none of them has a
              single main. It is the number the match-ingest claim weighs each region's
              share of a batch by: <code>(1 + deficit) / Σ(1 + deficit)</code>, shown as
              <em>Claim share</em>, computed with the ingestor's own arithmetic over the
              same counts.
            </p>
            <p>
              <em>Below target</em> is the share of those champions under the target in
              that region — the single number that made the original imbalance legible.
            </p>
            <p>
              Matches are ranked games counted by the day they were <em>ingested</em>,
              not played: that is the pipeline's spend, which is what the claim balances.
              The target and the claimed regions are read from the configuration the
              ingestor published at its last boot.
            </p>
          </template>
        </PanelTitle>
        <span class="text-xs text-dimmed">
          measured {{ formatTimeAgo(balance.measuredAtUtc) }}
          <template v-if="balance.configurationCapturedAtUtc">
            · ingestor config from {{ formatTimeAgo(balance.configurationCapturedAtUtc) }}
            ({{ formatDateTime(balance.configurationCapturedAtUtc) }})
          </template>
        </span>
      </div>
    </template>

    <p v-if="balance.unknownReason" class="text-sm text-dimmed italic">
      Region balance could not be measured: {{ balance.unknownReason }}
    </p>

    <template v-else>
      <p v-if="balance.coverageUnknownReason" class="mb-3 text-xs text-dimmed italic">
        Coverage not shown: {{ balance.coverageUnknownReason }}
      </p>
      <p v-else class="mb-3 text-xs text-muted tabular-nums">
        Target {{ formatNumber(balance.targetMainsPerChampion) }} active mains per champion ·
        {{ formatNumber(balance.championUniverse) }} champions with a main somewhere
      </p>

      <div class="overflow-x-auto">
        <table class="w-full text-sm tabular-nums">
          <thead>
            <tr class="text-xs text-muted uppercase text-right">
              <th class="py-1.5 pr-3 text-left font-normal">
                Region
              </th>
              <th class="py-1.5 px-3 font-normal">
                Accounts
              </th>
              <th class="py-1.5 px-3 font-normal">
                Active mains
              </th>
              <th class="py-1.5 px-3 font-normal">
                Matches
              </th>
              <th class="py-1.5 px-3 font-normal">
                Share
              </th>
              <th v-if="coverageKnown" class="py-1.5 px-3 font-normal">
                Deficit
              </th>
              <th v-if="coverageKnown" class="py-1.5 px-3 font-normal">
                Below target
              </th>
              <th v-if="coverageKnown" class="py-1.5 pl-3 font-normal">
                Claim share
              </th>
            </tr>
          </thead>
          <tbody class="divide-y divide-default">
            <tr
              v-for="platform in balance.platforms"
              :key="platform.platformId"
              class="text-right"
              :class="{ 'text-dimmed': platform.inClaim === false }"
            >
              <td class="py-1.5 pr-3 text-left">
                <span class="font-medium" :class="platform.inClaim === false ? '' : 'text-highlighted'">
                  {{ platform.platformId }}
                </span>
                <span v-if="platform.inClaim !== true" class="ml-1.5 text-xs text-dimmed">
                  {{ claimLabel(platform) }}
                </span>
              </td>
              <td class="py-1.5 px-3">
                {{ formatNumber(platform.accounts) }}
              </td>
              <td class="py-1.5 px-3">
                {{ formatNumber(platform.activeMainAccounts) }}
              </td>
              <td class="py-1.5 px-3">
                {{ formatNumber(platform.matchesInWindow) }}
              </td>
              <td class="py-1.5 px-3">
                {{ formatPercentOrDash(platform.matchShare, 0) }}
              </td>
              <td v-if="coverageKnown" class="py-1.5 px-3">
                {{ formatPercentOrDash(platform.meanCoverageDeficit, 0) }}
              </td>
              <td v-if="coverageKnown" class="py-1.5 px-3">
                {{ formatPercentOrDash(platform.championsBelowTargetShare, 0) }}
                <span v-if="platform.championsBelowTarget !== null" class="text-xs text-dimmed">
                  ({{ formatNumber(platform.championsBelowTarget) }})
                </span>
              </td>
              <td v-if="coverageKnown" class="py-1.5 pl-3">
                {{ formatPercentOrDash(platform.claimShare, 0) }}
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div class="mt-5">
        <PanelTitle
          variant="label"
          title="Matches ingested per day, by region"
          class="mb-1.5"
        />
        <ChartsBarChart
          :data="chartData"
          :height="220"
          :categories="chartCategories"
          :y-axis="platforms"
          :stacked="true"
          :x-num-ticks="Math.min(chartData.length, 7)"
          :x-formatter="xFormatter"
          :y-formatter="formatCount"
          :tooltip-title-formatter="labelTooltipTitle"
          v-bind="multiTimeBarProps()"
        />
      </div>
    </template>
  </UCard>
</template>
