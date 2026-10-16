<script setup lang="ts">
import type { StaticItemData } from '#shared/types/static-data'
import type { LandingOverlayPanelKind } from './LandingOverlayPanel.vue'

/**
 * The in-game moment: the overlay's four panels arrive on the screen one by
 * one as the visitor scrolls through what each does. On a wide screen the
 * screen stays pinned beside the steps and the step crossing the middle of the
 * viewport is the one drawn; on a narrow one, or without motion, the screen
 * shows every panel at once above the list.
 */
defineProps<{ patch: string | null, items: Record<number, StaticItemData> | null }>()

const STEPS: { panel: LandingOverlayPanelKind, title: string, body: string, keys?: string }[] = [
  {
    panel: 'win-probability',
    title: 'Who is ahead, all game',
    body: 'Both sides\' chance to win, from every lane\'s creep score, level and kills and from the map: turrets, drakes and the soul, Baron and Elder while they last.',
  },
  {
    panel: 'pace',
    title: 'Your pace against your tier',
    body: 'CS and gold per minute with their curve, beside the median of games at your Solo/Duo rank at the same minute.',
  },
  {
    panel: 'next-item',
    title: 'What to buy next',
    body: 'The legendary the true mains of your champion complete next from where your build stands, and the gold it still takes. The starter until you have one.',
  },
  {
    panel: 'item-value',
    title: 'Who has the gold',
    body: 'Each team\'s item gold and each lane\'s, while you hold the key. Every panel can be always on, held or toggled by a key of your choice.',
    keys: 'TAB',
  },
]

const reduced = useReducedMotion()
const wide = ref(false)
const active = ref(STEPS.length - 1)
const steps = ref<HTMLElement[]>([])

onMounted(() => {
  const query = window.matchMedia('(min-width: 1024px)')
  wide.value = query.matches
  const update = (event: MediaQueryListEvent) => (wide.value = event.matches)
  query.addEventListener('change', update)

  // The step drawn is the last one whose text has risen past the middle of the viewport, beside the
  // pinned screen — its centre, not its top: a step is a tall block with its text in the middle, so
  // its top crosses long before the visitor reaches the words. Read on scroll rather than from
  // intersections, which a fast scroll (or a jump to an anchor) can skip entirely.
  let frame = 0
  const read = () => {
    frame = 0
    const line = window.innerHeight * 0.55
    const passed = steps.value.filter((step) => {
      const box = step.getBoundingClientRect()
      return box.top + box.height / 2 < line
    }).length
    active.value = Math.max(0, passed - 1)
  }
  const onScroll = () => (frame ||= requestAnimationFrame(read))
  window.addEventListener('scroll', onScroll, { passive: true })
  read()
  onBeforeUnmount(() => {
    window.removeEventListener('scroll', onScroll)
    cancelAnimationFrame(frame)
    query.removeEventListener('change', update)
  })
})

const scrolly = computed(() => wide.value && !reduced.value)
const current = computed(() => (scrolly.value ? active.value : STEPS.length - 1))
const shown = computed(() => STEPS.slice(0, current.value + 1).map(step => step.panel))
const tabHeld = computed(() => (scrolly.value ? current.value === STEPS.length - 1 : true))
</script>

<template>
  <div class="grid gap-10 lg:grid-cols-[minmax(0,20rem)_minmax(0,1fr)] lg:gap-14">
    <ol class="order-2 flex flex-col gap-8 lg:order-1 lg:gap-0">
      <li
        v-for="(step, index) in STEPS"
        :key="step.panel"
        ref="steps"
        class="transition-opacity duration-300 lg:flex lg:min-h-[62vh] lg:flex-col lg:justify-center"
        :class="scrolly && index !== current ? 'lg:opacity-35' : 'opacity-100'"
      >
        <p class="flex items-center gap-2 text-sm tabular-nums text-dimmed">
          <span class="font-medium text-primary">{{ index + 1 }}</span>
          <span class="h-px w-6 bg-white/15" />
          <kbd v-if="step.keys" class="keycap rounded bg-elevated px-1.5 py-px font-mono text-[10px] font-semibold text-highlighted">{{ step.keys }}</kbd>
        </p>
        <h3 class="mt-3 text-lg font-semibold text-highlighted">
          {{ step.title }}
        </h3>
        <p class="mt-2 text-sm leading-relaxed text-muted text-pretty">
          {{ step.body }}
        </p>
      </li>
    </ol>

    <div class="order-1 lg:order-2">
      <div class="lg:sticky lg:top-[max(6rem,calc(50vh-16rem))]">
        <DesktopLandingGameScreen :shown="shown" :tab-held="tabHeld" :patch="patch" :items="items" aria-hidden="true" />
      </div>
    </div>
  </div>
</template>
