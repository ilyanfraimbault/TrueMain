<script setup lang="ts">
import type { ChampionOverviewRow } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { formatCompactCount } from '~~/shared/utils/counts'
import { championSplashUrlFromIcon, formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#shared/utils/positions'
import { banRateTone, pickRateTone, winRateTone } from '#common/utils/rate-tone'

// Variant D ("Cockpit"): the patch's meta as a master–detail panel. On the
// left, the overview's top rows — the most played and banned champions, a
// presence ranking (#2356), so it is never labelled as strength — each with its
// win, pick and ban rate as a figure over a fine bar, re-sortable in place (the
// rows glide to their new places). On the right, the row under the pointer or
// the keyboard, on its splash art: a card that tilts toward the cursor, its
// rates gliding to the new values. Every figure is the overview payload's.
const props = defineProps<{
  rows: ChampionOverviewRow[]
  pending: boolean
  championsById: Map<number, ChampionStaticListItem>
  patch: string | null
}>()

type SortKey = 'games' | 'winRate' | 'pickRate' | 'banRate'

const SORTS: { key: SortKey, label: string }[] = [
  { key: 'games', label: 'Played' },
  { key: 'winRate', label: 'Win' },
  { key: 'pickRate', label: 'Pick' },
  { key: 'banRate', label: 'Ban' },
]

const { pathFor } = useChampionSlugs()
const canonicalIcon = useCanonicalIcon()
const splashOf = useCanonicalSplash()

const enriched = computed(() => props.rows.map((row) => {
  const champion = props.championsById.get(row.championId)
  const lane = POSITION_BY_VALUE.get(row.position)
  return {
    ...row,
    key: `${row.championId}-${row.position}`,
    name: championDisplayName(row.championId, champion),
    icon: canonicalIcon(champion?.iconUrl),
    splash: splashOf(championSplashUrlFromIcon(champion?.iconUrl)),
    lane,
    to: { path: pathFor(row.championId), query: { position: row.position } },
  }
}))

type MetaRow = (typeof enriched.value)[number]

const sort = ref<SortKey>('games')
// Rows rise in once, with the first data. A re-sort moves the row nodes, and a
// moved node replays its CSS animation, so the entrance is switched off first.
const settled = ref(false)
function setSort(key: SortKey) {
  settled.value = true
  sort.value = key
}
const sorted = computed(() => {
  // `games` is the payload's own order (tier, then busiest first).
  if (sort.value === 'games') return enriched.value
  const key = sort.value
  return [...enriched.value].sort((a, b) => (b[key] ?? -1) - (a[key] ?? -1))
})

const maxPick = computed(() => Math.max(...props.rows.map(r => r.pickRate), 0) || 1)
const maxBan = computed(() => Math.max(...props.rows.map(r => r.banRate ?? 0), 0) || 1)

/** A win rate as a bar out of the middle: ±6 points fill a half. */
const WIN_SPAN = 0.06
function winBar(winRate: number) {
  const t = Math.max(-1, Math.min(1, (winRate - 0.5) / WIN_SPAN))
  return {
    left: `${t >= 0 ? 50 : 50 + t * 50}%`,
    width: `${Math.abs(t) * 50}%`,
    negative: t < 0,
  }
}

const pct = (value: number) => formatPercentage(value)

function rowLabel(row: MetaRow): string {
  const parts = [row.name, row.lane?.label, `tier ${row.tier}`, `win rate ${pct(row.winRate)}`, `pick rate ${pct(row.pickRate)}`]
  if (row.banRate !== null) parts.push(`ban rate ${pct(row.banRate)}`)
  return parts.filter(Boolean).join(', ')
}

// ─── Selection & the preview's art ────────────────────────────────────────
const selectedKey = ref<string | null>(null)
const selected = computed<MetaRow | null>(() =>
  enriched.value.find(row => row.key === selectedKey.value) ?? sorted.value[0] ?? null)

function select(row: MetaRow) {
  selectedKey.value = row.key
}

// Two stacked pictures cross-fade: the next splash is decoded off-screen
// first, so the swap never shows a half-loaded or blank frame, and only the
// last row asked for is ever shown when the pointer runs down the list.
const art = reactive({ a: '', b: '', front: 'a' as 'a' | 'b' })
let artToken = 0
const preloaded = new Map<string, Promise<void>>()

function preload(src: string): Promise<void> {
  let job = preloaded.get(src)
  if (!job) {
    const probe = new Image()
    probe.decoding = 'async'
    probe.src = src
    job = probe.decode().catch(() => {})
    preloaded.set(src, job)
  }
  return job
}

async function showArt(src: string | null) {
  if (!src || !import.meta.client) return
  const current = art.front === 'a' ? art.a : art.b
  if (src === current) return
  const token = ++artToken
  await preload(src)
  if (token !== artToken) return
  if (art.front === 'a') {
    art.b = src
    art.front = 'b'
  }
  else {
    art.a = src
    art.front = 'a'
  }
}

watch(() => selected.value?.splash, src => showArt(src ?? null), { immediate: true })

/** The first time the pointer reaches the list, warm every row's splash. */
let warmed = false
function warm() {
  if (warmed) return
  warmed = true
  for (const row of sorted.value) if (row.splash) preload(row.splash)
}

// Bars grow in once, when the panel first comes into view.
const panel = ref<HTMLElement | null>(null)
const live = ref(false)
let observer: IntersectionObserver | null = null
watch(() => props.pending, async (pending) => {
  if (pending || live.value) return
  await nextTick()
  if (!panel.value) return
  observer?.disconnect()
  observer = new IntersectionObserver(([entry]) => {
    // Delivered after a rendering step: the empty bars have been styled once,
    // so they grow from there.
    if (!entry?.isIntersecting) return
    live.value = true
    observer?.disconnect()
  }, { threshold: 0.2 })
  observer.observe(panel.value)
}, { immediate: true })
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <section
    ref="panel"
    class="d-meta d-panel"
    :class="{ 'is-live': live, 'is-settled': settled }"
    aria-labelledby="d-meta-title"
  >
    <span
      class="d-ring"
      data-d-spot
      aria-hidden="true"
    />

    <header class="d-phead">
      <div class="min-w-0">
        <h2
          id="d-meta-title"
          class="d-phead__title"
        >
          Meta
        </h2>
        <p class="d-phead__sub">
          Most played and banned<template v-if="patch">
            · patch {{ patch }}
          </template>
        </p>
      </div>
      <div
        class="d-seg"
        role="group"
        aria-label="Sort the rows by"
      >
        <button
          v-for="option in SORTS"
          :key="option.key"
          type="button"
          class="d-seg__btn"
          :aria-pressed="sort === option.key"
          @click="setSort(option.key)"
        >
          {{ option.label }}
        </button>
      </div>
    </header>

    <div class="d-meta__body">
      <div
        class="d-meta__list"
        @pointerenter="warm"
      >
        <div
          class="d-mrow d-mrow--head"
          aria-hidden="true"
        >
          <span>Champion</span>
          <span class="d-mrow__tier">Tier</span>
          <span>Win</span>
          <span class="d-mrow__pick">Pick</span>
          <span class="d-mrow__ban">Ban</span>
        </div>

        <ul
          v-if="pending"
          class="d-meta__rows"
          aria-hidden="true"
        >
          <li
            v-for="i in 8"
            :key="i"
            class="d-mrow d-mrow--skeleton"
          >
            <span class="flex items-center gap-3">
              <USkeleton class="size-9 rounded-lg" />
              <span class="flex flex-col gap-1.5">
                <USkeleton class="h-3.5 w-24" />
                <USkeleton class="h-2.5 w-16" />
              </span>
            </span>
            <USkeleton class="d-mrow__tier mx-auto h-4 w-4" />
            <USkeleton class="h-3 w-12" />
            <USkeleton class="d-mrow__pick h-3 w-10" />
            <USkeleton class="d-mrow__ban h-3 w-10" />
          </li>
        </ul>

        <TransitionGroup
          v-else-if="sorted.length > 0"
          tag="ul"
          name="d-row"
          class="d-meta__rows"
        >
          <li
            v-for="(row, index) in sorted"
            :key="row.key"
            :style="{ '--i': index }"
          >
            <NuxtLink
              :to="row.to"
              class="d-mrow"
              :class="{ 'is-selected': selected?.key === row.key }"
              :aria-label="rowLabel(row)"
              @pointerenter="select(row)"
              @focus="select(row)"
            >
              <span class="d-mrow__champ">
                <img
                  v-if="row.icon"
                  :src="row.icon"
                  alt=""
                  width="36"
                  height="36"
                  class="d-mrow__icon img-skeleton"
                >
                <span
                  v-else
                  class="d-mrow__icon img-skeleton"
                />
                <span class="d-mrow__id">
                  <span class="d-mrow__name">{{ row.name }}</span>
                  <span class="d-mrow__sub">
                    <img
                      v-if="row.lane?.iconUrl"
                      :src="row.lane.iconUrl"
                      alt=""
                      width="12"
                      height="12"
                      class="size-3 opacity-70"
                    >
                    {{ row.lane?.label ?? row.position }}
                    <span class="d-mrow__games"><span aria-hidden="true">·</span> {{ formatCompactCount(row.games) }} games</span>
                  </span>
                </span>
              </span>

              <span class="d-mrow__tier"><TierBadge :tier="row.tier" /></span>

              <span class="d-stat">
                <span
                  class="d-stat__value"
                  :class="winRateTone(row.winRate)"
                >{{ pct(row.winRate) }}</span>
                <span class="d-bar d-bar--mid">
                  <span
                    class="d-bar__fill"
                    :class="{ 'is-neg': winBar(row.winRate).negative }"
                    :style="{ left: winBar(row.winRate).left, width: winBar(row.winRate).width }"
                  />
                </span>
              </span>

              <span class="d-stat d-mrow__pick">
                <span
                  class="d-stat__value"
                  :class="pickRateTone(row.pickRate)"
                >{{ pct(row.pickRate) }}</span>
                <span class="d-bar">
                  <span
                    class="d-bar__fill"
                    :style="{ width: `${(row.pickRate / maxPick) * 100}%` }"
                  />
                </span>
              </span>

              <span class="d-stat d-mrow__ban">
                <template v-if="row.banRate !== null">
                  <span
                    class="d-stat__value"
                    :class="banRateTone(row.banRate)"
                  >{{ pct(row.banRate) }}</span>
                  <span class="d-bar">
                    <span
                      class="d-bar__fill d-bar__fill--soft"
                      :style="{ width: `${(row.banRate / maxBan) * 100}%` }"
                    />
                  </span>
                </template>
                <span
                  v-else
                  class="d-stat__value text-dimmed"
                >—</span>
              </span>
            </NuxtLink>
          </li>
        </TransitionGroup>

        <UEmpty
          v-else
          size="sm"
          variant="naked"
          icon="i-lucide-trending-up"
          description="No champion stats for this patch yet."
        />

        <NuxtLink
          to="/champions/tierlist"
          class="d-plink"
        >
          Full tier list
          <UIcon
            name="i-lucide-arrow-right"
            class="size-3.5"
          />
        </NuxtLink>
      </div>

      <div class="d-meta__aside">
        <USkeleton
          v-if="pending || !selected"
          class="d-preview d-preview--skeleton"
        />
        <NuxtLink
          v-else
          v-tilt="4"
          :to="selected.to"
          class="d-preview"
          :aria-label="`Open the ${selected.name} build`"
        >
          <span
            class="d-preview__art"
            aria-hidden="true"
          >
            <img
              v-if="art.a"
              :src="art.a"
              alt=""
              class="d-preview__img"
              :class="{ 'is-front': art.front === 'a' }"
            >
            <img
              v-if="art.b"
              :src="art.b"
              alt=""
              class="d-preview__img"
              :class="{ 'is-front': art.front === 'b' }"
            >
            <span class="d-preview__shade" />
            <span class="d-preview__glare" />
          </span>

          <span class="d-preview__top">
            <span class="d-preview__lane">
              <img
                v-if="selected.lane?.iconUrl"
                :src="selected.lane.iconUrl"
                alt=""
                width="14"
                height="14"
                class="size-3.5"
              >
              {{ selected.lane?.label ?? selected.position }}
            </span>
            <TierBadge
              :tier="selected.tier"
              stacked
            />
          </span>

          <span class="d-preview__body">
            <Transition
              name="d-swap"
              mode="out-in"
            >
              <span
                :key="selected.key"
                class="d-preview__name"
              >{{ selected.name }}</span>
            </Transition>
            <span class="d-preview__stats">
              <span class="d-preview__stat">
                <span
                  class="d-preview__value"
                  :class="winRateTone(selected.winRate)"
                ><FxCount
                  :value="selected.winRate"
                  :format="pct"
                  :duration="700"
                /></span>
                <span class="d-preview__label">Win</span>
              </span>
              <span class="d-preview__stat">
                <span class="d-preview__value"><FxCount
                  :value="selected.pickRate"
                  :format="pct"
                  :duration="700"
                /></span>
                <span class="d-preview__label">Pick</span>
              </span>
              <span class="d-preview__stat">
                <span
                  v-if="selected.banRate !== null"
                  class="d-preview__value"
                ><FxCount
                  :value="selected.banRate"
                  :format="pct"
                  :duration="700"
                /></span>
                <span
                  v-else
                  class="d-preview__value text-dimmed"
                >—</span>
                <span class="d-preview__label">Ban</span>
              </span>
            </span>
            <span class="d-preview__cta">
              Open build
              <UIcon
                name="i-lucide-arrow-up-right"
                class="size-4"
              />
            </span>
          </span>
        </NuxtLink>
      </div>
    </div>
  </section>
</template>
