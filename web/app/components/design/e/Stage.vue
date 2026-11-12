<script setup lang="ts">
import { useMediaQuery } from '@vueuse/core'
import type { ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { formatCompactCount, formatCount } from '~~/shared/utils/counts'
import { championLoadingUrlFromIcon, championSplashUrlFromIcon, formatPercentage } from '~~/shared/utils/ddragon'
import { POSITION_BY_VALUE } from '#shared/utils/positions'
import { winRateTone } from '#common/utils/rate-tone'
import { trackPointer } from './light-engine'

// The home's champion stage (≥768px), the launcher's "featured" screen: the
// patch's most present champion fills the stage on its splash art, its numbers
// in the panel on the left, the other top champions in a dock along the
// bottom. Hovering or focusing a card previews it; clicking opens its build.
// The stage moves on by itself every 7 s (the bar under the current card) and
// holds still while the reader is on the panel or the dock, has keyboard focus
// in it, has scrolled it away, has switched tabs, or asked for less motion.
//
// The art: two layers, the next splash decoded on the hidden one before it
// fades in over the current, then the following one preloaded. A slow push-in
// (Ken Burns) runs on each picture, and the art leans a few pixels against the
// cursor (the shared light engine's tracker — no listener of its own).
const props = defineProps<{
  rows: ChampionOverviewRow[]
  championsById: Map<number, ChampionStaticListItem>
  pending: boolean
  patch: string | null
  gamesAnalyzed: number
}>()

const { pathFor } = useChampionSlugs()
const splashOf = useCanonicalSplash()
const artOf = useCanonicalLoadingArt()
const fx = useFx()

const slides = computed(() => props.rows.map((row) => {
  const champion = props.championsById.get(row.championId)
  const lane = POSITION_BY_VALUE.get(row.position) ?? null
  return {
    key: `${row.championId}-${row.position}`,
    row,
    name: championDisplayName(row.championId, champion),
    lane,
    laneLabel: lane?.label ?? row.position,
    splash: splashOf(championSplashUrlFromIcon(champion?.iconUrl)),
    art: artOf(championLoadingUrlFromIcon(champion?.iconUrl)),
    to: { path: pathFor(row.championId), query: { position: row.position } },
  }
}))
const count = computed(() => slides.value.length)

// Kept across a trip to another page: the stage reopens on the champion left.
const index = useState('e-stage-index', () => 0)
const current = computed(() => (count.value ? slides.value[Math.min(index.value, count.value - 1)] ?? null : null))
watch(count, (n) => {
  if (n && index.value >= n) index.value = 0
})

const pct = (value: number) => formatPercentage(value)

// ─── Run state ──────────────────────────────────────────────────────────
const mounted = ref(false)
const desktop = useMediaQuery('(min-width: 768px)')
const hoverInfo = ref(false)
const hoverDock = ref(false)
const keyboardInside = ref(false)
const offscreen = ref(false)
const docHidden = ref(false)
const userPaused = ref(false)

const autoplay = computed(() => mounted.value && desktop.value && count.value > 1 && !fx.value.reducedMotion)
const running = computed(() => autoplay.value && !userPaused.value && !hoverInfo.value && !hoverDock.value
  && !keyboardInside.value && !offscreen.value && !docHidden.value)

// Bumped on every selection: re-keys the progress bar, so its 7 s restart.
const cycle = ref(0)

function select(i: number) {
  const n = count.value
  if (!n) return
  index.value = ((i % n) + n) % n
  cycle.value++
}

function advance() {
  select(index.value + 1)
}

// ─── Art ────────────────────────────────────────────────────────────────
const layers = reactive([
  { src: null as string | null, id: 0 },
  { src: null as string | null, id: 0 },
])
const front = ref(0)
const imgEls: (HTMLImageElement | null)[] = [null, null]
let presentToken = 0
const preloaded = new Set<string>()

function preload(src: string | null | undefined) {
  if (!src || preloaded.has(src)) return
  preloaded.add(src)
  const img = new Image()
  img.decoding = 'async'
  img.src = src
}

async function present(src: string | null | undefined) {
  if (!src || !mounted.value || !desktop.value) return
  const token = ++presentToken
  if (layers[front.value]!.src === src) return
  const back = 1 - front.value
  const layer = layers[back]!
  layer.src = src
  layer.id++
  preloaded.add(src)
  await nextTick()
  try {
    await imgEls[back]?.decode()
  }
  catch {
    // Interrupted or broken: show whatever the element has.
  }
  if (token !== presentToken) return
  front.value = back
  if (count.value > 1) preload(slides.value[(index.value + 1) % count.value]?.splash)
}

watch([() => current.value?.splash, desktop, mounted], ([src]) => {
  void present(src)
})

function setImg(i: number, el: unknown) {
  imgEls[i] = (el as HTMLImageElement | null) ?? null
}

// ─── Dock: hover intent, keyboard, keeping the current card in view ─────
const stripEl = ref<HTMLElement | null>(null)
let intentTimer: ReturnType<typeof setTimeout> | undefined

function onCardEnter(i: number, event: PointerEvent) {
  if (event.pointerType === 'touch') return
  preload(slides.value[i]?.splash)
  clearTimeout(intentTimer)
  intentTimer = setTimeout(() => select(i), 90)
}

function onCardLeave() {
  clearTimeout(intentTimer)
}

function cards(): HTMLElement[] {
  return Array.from(stripEl.value?.querySelectorAll<HTMLElement>('[data-e-card]') ?? [])
}

function focusCard(i: number) {
  const n = count.value
  if (!n) return
  const target = cards()[((i % n) + n) % n]
  target?.focus({ preventScroll: true })
}

function onStripKeydown(event: KeyboardEvent) {
  if (event.altKey || event.ctrlKey || event.metaKey) return
  const moves: Record<string, () => number> = {
    ArrowRight: () => index.value + 1,
    ArrowDown: () => index.value + 1,
    ArrowLeft: () => index.value - 1,
    ArrowUp: () => index.value - 1,
    Home: () => 0,
    End: () => count.value - 1,
  }
  const move = moves[event.key]
  if (!move) return
  event.preventDefault()
  focusCard(move())
}

function keepCurrentInView() {
  const strip = stripEl.value
  const card = cards()[index.value]
  if (!strip || !card || strip.scrollWidth <= strip.clientWidth) return
  const left = card.offsetLeft - strip.offsetLeft
  const right = left + card.offsetWidth
  const pad = 24
  let target: number | null = null
  if (left - pad < strip.scrollLeft) target = left - pad
  else if (right + pad > strip.scrollLeft + strip.clientWidth) target = right + pad - strip.clientWidth
  if (target !== null) strip.scrollTo({ left: Math.max(0, target), behavior: fx.value.reducedMotion ? 'auto' : 'smooth' })
}

watch(index, () => nextTick(keepCurrentInView))

// ─── Pausing ────────────────────────────────────────────────────────────
const stageEl = ref<HTMLElement | null>(null)

function onFocusIn(event: FocusEvent) {
  keyboardInside.value = (event.target as HTMLElement).matches(':focus-visible')
}

function onFocusOut(event: FocusEvent) {
  const next = event.relatedTarget as Node | null
  if (!next || !stageEl.value?.contains(next)) keyboardInside.value = false
}

function onHover(zone: 'info' | 'dock', inside: boolean, event: PointerEvent) {
  if (event.pointerType === 'touch') return
  if (zone === 'info') hoverInfo.value = inside
  else hoverDock.value = inside
}

let observer: IntersectionObserver | null = null
let untrack: (() => void) | null = null
const onVisibility = () => (docHidden.value = document.hidden)

onMounted(() => {
  mounted.value = true
  docHidden.value = document.hidden
  document.addEventListener('visibilitychange', onVisibility)

  if (stageEl.value) {
    observer = new IntersectionObserver(([entry]) => {
      offscreen.value = !entry || entry.intersectionRatio < 0.35
    }, { threshold: [0, 0.35, 0.7] })
    observer.observe(stageEl.value)
  }

  // The lean: a few pixels against the cursor, eased by CSS; flat when the
  // cursor is elsewhere. Full effects only.
  const el = stageEl.value
  if (el && fx.value.level === 'full' && !fx.value.reducedMotion) {
    let lastX = 0
    let lastY = 0
    untrack = trackPointer(el, ({ u, v, inside }) => {
      const x = inside ? Math.max(-0.5, Math.min(0.5, u - 0.5)) : 0
      const y = inside ? Math.max(-0.5, Math.min(0.5, v - 0.5)) : 0
      if (Math.abs(x - lastX) < 0.002 && Math.abs(y - lastY) < 0.002) return
      lastX = x
      lastY = y
      el.style.setProperty('--e-px', x.toFixed(3))
      el.style.setProperty('--e-py', y.toFixed(3))
    })
  }
})

onBeforeUnmount(() => {
  clearTimeout(intentTimer)
  observer?.disconnect()
  untrack?.()
  document.removeEventListener('visibilitychange', onVisibility)
  presentToken++
})
</script>

<template>
  <section
    ref="stageEl"
    class="e-stage"
    :class="{ 'is-running': running, 'is-offscreen': offscreen, 'is-ready': !pending && current }"
    aria-roledescription="carousel"
    aria-label="Most played and banned champions"
    @focusin="onFocusIn"
    @focusout="onFocusOut"
  >
    <div
      class="e-stage__art"
      aria-hidden="true"
    >
      <div class="e-stage__parallax">
        <div
          v-for="(layer, i) in layers"
          :key="i"
          class="e-stage__layer"
          :class="{ 'is-front': i === front && layer.src }"
        >
          <img
            v-if="layer.src"
            :key="layer.id"
            :ref="(el) => setImg(i, el)"
            :src="layer.src"
            alt=""
            decoding="async"
            class="e-stage__img"
          >
        </div>
      </div>
      <span class="e-stage__shade" />
    </div>

    <!-- Loading: the same frame, so nothing moves when the data lands. -->
    <div
      v-if="pending"
      class="e-stage__body"
      aria-hidden="true"
    >
      <div class="e-stage__info">
        <USkeleton class="h-6 w-56 rounded-full" />
        <USkeleton class="mt-6 h-16 w-80 max-w-full" />
        <USkeleton class="mt-4 h-5 w-64" />
        <div class="mt-9 flex gap-10">
          <USkeleton
            v-for="i in 3"
            :key="i"
            class="h-11 w-20"
          />
        </div>
        <div class="mt-9 flex gap-3">
          <USkeleton class="h-13 w-44 rounded-xl" />
          <USkeleton class="h-13 w-36 rounded-xl" />
        </div>
      </div>
      <div class="e-stage__dock">
        <div class="e-stage__strip">
          <span
            v-for="i in 8"
            :key="i"
            class="e-thumb e-thumb--skeleton"
          />
        </div>
      </div>
    </div>

    <div
      v-else-if="current"
      class="e-stage__body"
    >
      <div
        class="e-stage__info"
        @pointerenter="onHover('info', true, $event)"
        @pointerleave="onHover('info', false, $event)"
      >
        <p class="e-stage__eyebrow">
          <span
            v-if="patch"
            class="e-chip"
          >Patch {{ patch }}</span>
          <span>Most played and banned</span>
        </p>

        <div class="e-stage__titlebox">
          <Transition name="e-swap">
            <div
              :key="current.key"
              class="e-stage__title"
            >
              <h2
                class="e-stage__name"
                :class="{ 'is-long': current.name.length > 10 }"
              >
                {{ current.name }}
              </h2>
            <p class="e-stage__meta">
              <span class="e-stage__lane">
                <img
                  v-if="current.lane?.iconUrl"
                  :src="current.lane.iconUrl"
                  alt=""
                  width="16"
                  height="16"
                >
                {{ current.laneLabel }}
              </span>
              <span
                class="e-stage__sep"
                aria-hidden="true"
              />
              <span class="e-stage__tier">
                <TierBadge :tier="current.row.tier" />
                <span>tier</span>
              </span>
              <span
                class="e-stage__sep"
                aria-hidden="true"
              />
              <span>{{ formatCount(current.row.games) }} games</span>
            </p>
          </div>
        </Transition>
        </div>

        <dl class="e-stage__stats">
          <div class="e-stat">
            <dt class="e-stat__label">
              Win rate
            </dt>
            <dd
              class="e-stat__value"
              :class="winRateTone(current.row.winRate)"
            >
              <FxCount
                :value="current.row.winRate"
                :format="pct"
                :duration="750"
              />
            </dd>
          </div>
          <div class="e-stat">
            <dt class="e-stat__label">
              Pick rate
            </dt>
            <dd class="e-stat__value">
              <FxCount
                :value="current.row.pickRate"
                :format="pct"
                :duration="750"
              />
            </dd>
          </div>
          <div class="e-stat">
            <dt class="e-stat__label">
              Ban rate
            </dt>
            <dd class="e-stat__value">
              <FxCount
                v-if="current.row.banRate !== null"
                :value="current.row.banRate"
                :format="pct"
                :duration="750"
              />
              <span v-else>—</span>
            </dd>
          </div>
        </dl>

        <div class="e-stage__actions">
          <UButton
            :to="current.to"
            size="xl"
            trailing-icon="i-lucide-arrow-right"
            label="Open build"
            :aria-label="`Open the ${current.name} build`"
            class="e-play"
          />
          <UButton
            to="/champions/tierlist"
            color="neutral"
            variant="ghost"
            size="xl"
            label="Tier list"
            class="e-ghost"
          />
        </div>
      </div>

      <div
        class="e-stage__dock"
        @pointerenter="onHover('dock', true, $event)"
        @pointerleave="onHover('dock', false, $event)"
      >
        <ul
          ref="stripEl"
          class="e-stage__strip"
          aria-label="Top champions this patch"
          @keydown="onStripKeydown"
        >
          <li
            v-for="(slide, i) in slides"
            :key="slide.key"
          >
            <NuxtLink
              :to="slide.to"
              class="e-thumb"
              :class="{ 'is-current': i === index }"
              :tabindex="i === index ? 0 : -1"
              :aria-current="i === index ? 'true' : undefined"
              :aria-label="`${slide.name}, ${slide.laneLabel}: open the build`"
              data-e-card
              @pointerenter="onCardEnter(i, $event)"
              @pointerleave="onCardLeave"
              @focus="select(i)"
            >
              <img
                v-if="slide.art"
                :src="slide.art"
                alt=""
                width="308"
                height="560"
                decoding="async"
                class="e-thumb__art img-skeleton"
              >
              <span
                class="e-thumb__shade"
                aria-hidden="true"
              />
              <img
                v-if="slide.lane?.iconUrl"
                :src="slide.lane.iconUrl"
                alt=""
                width="14"
                height="14"
                class="e-thumb__lane"
              >
              <span
                class="e-thumb__name"
                aria-hidden="true"
              >{{ slide.name }}</span>
              <span
                v-if="autoplay && !userPaused && i === index"
                :key="cycle"
                class="e-thumb__progress"
                aria-hidden="true"
                @animationend.self="advance"
              />
            </NuxtLink>
          </li>
        </ul>

        <div class="e-stage__controls">
          <UButton
            icon="i-lucide-chevron-left"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Previous champion"
            class="e-ctl"
            @click="select(index - 1)"
          />
          <UButton
            v-if="autoplay"
            :icon="userPaused ? 'i-lucide-play' : 'i-lucide-pause'"
            color="neutral"
            variant="ghost"
            size="sm"
            :aria-label="userPaused ? 'Start automatic rotation' : 'Stop automatic rotation'"
            class="e-ctl"
            @click="userPaused = !userPaused"
          />
          <UButton
            icon="i-lucide-chevron-right"
            color="neutral"
            variant="ghost"
            size="sm"
            aria-label="Next champion"
            class="e-ctl"
            @click="select(index + 1)"
          />
          <p
            v-if="gamesAnalyzed > 0"
            class="e-stage__total"
          >
            <span class="text-highlighted">{{ formatCompactCount(gamesAnalyzed) }}</span> main games analyzed
          </p>
        </div>
      </div>

      <p
        class="sr-only"
        :aria-live="running ? 'off' : 'polite'"
        aria-atomic="true"
      >
        {{ current.name }}, {{ current.laneLabel }}, {{ index + 1 }} of {{ count }}
      </p>
    </div>

    <div
      v-else
      class="e-stage__body e-stage__body--empty"
    >
      <div class="e-stage__info">
        <h2 class="e-stage__name e-stage__name--empty">
          No champion stats for this patch yet.
        </h2>
        <div class="e-stage__actions">
          <UButton
            to="/champions"
            size="xl"
            trailing-icon="i-lucide-arrow-right"
            label="Browse champions"
            class="e-play"
          />
        </div>
      </div>
    </div>
  </section>
</template>
