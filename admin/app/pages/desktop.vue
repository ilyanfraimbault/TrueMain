<script setup lang="ts">
// The desktop app (#1805): installers downloaded from the site, and what the
// installed apps did — counted by each app under an anonymous id and sent to the
// API, one document per install and day. Read-only.
import type { DesktopKeyUsage, DesktopShareRow } from '~~/shared/types/desktop'
import { formatDate, formatNumber } from '~~/shared/utils/format'

const WINDOW_ITEMS = [
  { label: '7 days', value: 7 },
  { label: '30 days', value: 30 },
  { label: '90 days', value: 90 },
]
const windowDays = ref(30)

const { data, pending, error, refresh } = useDesktopUsage(windowDays)

const measured = computed(() => Boolean(data.value?.earliestDayUtc))

// Each name says what was counted, not just where: a page is a view of it, a
// feature an occurrence in which the app did that for the player.
const PAGE_LABELS: Record<string, string> = {
  dashboard: 'Dashboard',
  champions: 'Champions',
  champion: 'Champion page',
  tierlist: 'Tier list',
  matchup: 'Matchup',
  truemains: 'Truemains',
  favorites: 'Favorites',
  draft: 'Champ select',
  game: 'Game',
  overlay: 'Overlay',
  recordings: 'Recordings',
  recording: 'Recording',
  clip: 'Clip',
}
const FEATURE_LABELS: Record<string, string> = {
  champSelect: 'Champion select followed',
  liveGame: 'Game followed live',
  overlayShown: 'Games with the overlay drawn',
  gameRecorded: 'Games recorded',
  siteOpened: 'Site pages opened',
}
const OS_LABELS: Record<string, string> = { macos: 'macOS', windows: 'Windows', linux: 'Linux' }

function installs(count: number): string {
  return `${formatNumber(count)} ${count === 1 ? 'install' : 'installs'}`
}

function minutes(total: number): string {
  if (total < 60) return `${Math.round(total)} min`
  const hours = Math.floor(total / 60)
  return `${hours} h ${String(Math.round(total % 60)).padStart(2, '0')}`
}

const cards = computed(() => {
  const usage = data.value
  const totals = usage?.totals
  const perDay = totals && totals.activeInstallDays > 0
    ? minutes(totals.openMinutes / totals.activeInstallDays)
    : '—'
  return [
    {
      title: 'Active today',
      icon: 'i-lucide-monitor-check',
      value: formatNumber(usage?.active.today ?? 0),
      hint: `${formatNumber(usage?.active.last7Days ?? 0)} in 7 days · ${formatNumber(usage?.active.last30Days ?? 0)} in 30`,
    },
    {
      title: 'Downloads',
      icon: 'i-lucide-download',
      value: formatNumber((totals?.downloadsMac ?? 0) + (totals?.downloadsWindows ?? 0)),
      hint: `${formatNumber(totals?.downloadsMac ?? 0)} macOS · ${formatNumber(totals?.downloadsWindows ?? 0)} Windows`,
    },
    {
      title: 'New installs',
      icon: 'i-lucide-sparkles',
      value: formatNumber(totals?.newInstalls ?? 0),
      hint: `${installs(totals?.installs ?? 0)} active in the window`,
    },
    {
      title: 'Open per active day',
      icon: 'i-lucide-timer',
      value: perDay,
      hint: `${formatNumber(totals?.launches ?? 0)} launches`,
    },
  ]
})

const days = computed(() => (data.value?.days ?? []).map(day => ({
  label: formatBucketLabel(day.dayUtc, 'day'),
  active: day.activeInstalls,
  mac: day.downloadsMac,
  windows: day.downloadsWindows,
})))
const dayXFormatter = computed(() => indexLabelFormatter(days.value, row => row.label))
const activeCategories = { active: { name: 'Active installs', color: CHART_PRIMARY } }
const downloadCategories = {
  mac: { name: 'macOS', color: CHART_SERIES[0] },
  windows: { name: 'Windows', color: CHART_SERIES[1] },
}

function keyRows(entries: DesktopKeyUsage[] | undefined, labels: Record<string, string>): DesktopShareRow[] {
  return (entries ?? []).map(entry => ({
    key: entry.key,
    label: labels[entry.key] ?? entry.key,
    value: entry.count,
    hint: entry.count > 0 ? installs(entry.installs) : undefined,
  }))
}

const versionRows = computed<DesktopShareRow[]>(() =>
  (data.value?.versions ?? []).map(share => ({ key: share.key, label: share.key, value: share.installs })))
const osRows = computed<DesktopShareRow[]>(() =>
  (data.value?.operatingSystems ?? []).map(share => ({
    key: share.key,
    label: OS_LABELS[share.key] ?? share.key,
    value: share.installs,
  })))
const downloadRows = computed<DesktopShareRow[]>(() =>
  (data.value?.downloadVersions ?? []).map(version => ({
    key: version.version,
    label: version.version,
    value: version.mac + version.windows,
    hint: `${formatNumber(version.mac)} macOS · ${formatNumber(version.windows)} Windows`,
  })))
const pageRows = computed(() => keyRows(data.value?.pages, PAGE_LABELS))
const featureRows = computed(() => keyRows(data.value?.features, FEATURE_LABELS))
</script>

<template>
  <UDashboardPanel id="desktop">
    <template #header>
      <UDashboardNavbar title="Desktop app" icon="i-lucide-monitor-down">
        <template #leading>
          <UDashboardSidebarCollapse />
        </template>
        <template #right>
          <USelect v-model="windowDays" :items="WINDOW_ITEMS" class="w-28" aria-label="Window" />
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
      <FetchErrorAlert v-if="error" :error="error" title="Failed to load the desktop app's usage" class="mb-6" />

      <UPageGrid class="lg:grid-cols-4 gap-4 sm:gap-6 lg:gap-px">
        <UPageCard
          v-for="card in cards"
          :key="card.title"
          :icon="card.icon"
          :title="card.title"
          variant="subtle"
          :ui="{
            container: 'gap-y-1.5',
            wrapper: 'items-start',
            leading: 'p-2.5 rounded-full bg-primary/10 ring ring-inset ring-primary/25 flex-col',
            title: 'font-normal text-muted text-xs uppercase',
          }"
          class="lg:rounded-none first:rounded-l-lg last:rounded-r-lg"
        >
          <div class="flex flex-col gap-0.5">
            <USkeleton v-if="pending && !data" class="h-8 w-20" />
            <span v-else class="text-2xl font-semibold text-highlighted tabular-nums">{{ card.value }}</span>
            <span v-if="data" class="text-xs text-dimmed">{{ card.hint }}</span>
          </div>
        </UPageCard>
      </UPageGrid>

      <UCard :ui="{ root: 'overflow-visible' }" class="mt-6">
        <template #header>
          <PanelTitle
            title="Over time"
            :subtitle="measured ? `Since ${formatDate(data?.earliestDayUtc)}, by UTC day.` : 'By UTC day.'"
            info="Active installs are the installs that reported at least once that day; an install open all day counts once. Downloads are the installer links followed on the site's download page, crawlers and link previews left out. Nothing is drawn before the first recorded day: the app reports from the version that sends usage counts on, and earlier days were not measured."
          />
        </template>

        <USkeleton v-if="pending && !data" class="h-[260px] w-full" />
        <div v-else-if="!measured" class="h-[200px] flex flex-col items-center justify-center gap-1 text-center text-sm text-muted">
          <p>Nothing recorded in this window yet.</p>
          <p class="text-xs text-dimmed">
            Downloads are counted from the site's download page, usage from apps that share anonymous usage data.
          </p>
        </div>
        <div v-else class="grid gap-6 lg:grid-cols-2">
          <div>
            <PanelTitle variant="label" title="Active installs per day" class="mb-1.5" />
            <ChartsBarChart
              :data="days"
              :height="220"
              :categories="activeCategories"
              :y-axis="['active']"
              :x-num-ticks="Math.min(days.length, 6)"
              :x-formatter="dayXFormatter"
              :y-formatter="formatCount"
              :tooltip-title-formatter="labelTooltipTitle"
              v-bind="timeBarProps()"
            />
          </div>
          <div>
            <PanelTitle variant="label" title="Downloads per day" class="mb-1.5" />
            <ChartsBarChart
              :data="days"
              :height="220"
              :categories="downloadCategories"
              :y-axis="['mac', 'windows']"
              :stacked="true"
              :x-num-ticks="Math.min(days.length, 6)"
              :x-formatter="dayXFormatter"
              :y-formatter="formatCount"
              :tooltip-title-formatter="labelTooltipTitle"
              v-bind="multiTimeBarProps()"
            />
          </div>
        </div>
      </UCard>

      <div class="mt-6 grid grid-cols-1 gap-4 sm:gap-6 lg:grid-cols-3">
        <DesktopShareList
          title="Versions in use"
          info="Each install active in the window, counted once under the version of its latest day."
          :rows="versionRows"
          empty-message="No install reported in this window."
        />
        <DesktopShareList
          title="Operating systems"
          info="Each install active in the window, counted once."
          :rows="osRows"
          empty-message="No install reported in this window."
        />
        <DesktopShareList
          title="Downloads by version"
          info="The version the download page served when the link was followed, newest first."
          :rows="downloadRows"
          empty-message="No download in this window."
        />
      </div>

      <div class="mt-6 grid grid-cols-1 gap-4 sm:gap-6 lg:grid-cols-2">
        <DesktopShareList
          title="Pages"
          info="Views of each page of the app, and how many installs opened it. A filter changed on a page is not a new view. Every page is listed, so an unused one shows as zero."
          :rows="pageRows"
          empty-message="No page view in this window."
        />
        <DesktopShareList
          title="Features"
          info="How often the app did each thing for the player, and for how many installs. The overlay counts once per game it was drawn in."
          :rows="featureRows"
          empty-message="No feature used in this window."
        />
      </div>
    </template>
  </UDashboardPanel>
</template>
