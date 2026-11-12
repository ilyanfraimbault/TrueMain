<script setup lang="ts">
// A figure on mechanical digit wheels. Each wheel carries the ten digits three
// times and rests on the middle copy, so the server render, the resting state
// and both ends of every spin all show the same digit: the wheels can roll in
// on first sight and roll again on hover without a frame where the figure is
// wrong or blank. The text itself is a screen-reader-only copy.
const props = defineProps<{
  /** The figure as printed (`5.5M`, `1,204`): digits roll, everything else stands. */
  value: string
}>()

interface Cell {
  key: number
  char: string
  digit: number | null
  /** Order among the digit wheels, for the stagger. */
  order: number
}

const cells = computed<Cell[]>(() => {
  let order = 0
  return [...props.value].map((char, key) => {
    const digit = /\d/.test(char) ? Number(char) : null
    return { key, char, digit, order: digit === null ? -1 : order++ }
  })
})
const wheelCount = computed(() => cells.value.filter(cell => cell.digit !== null).length)

const INTRO = { base: 1300, step: 170, delay: 80 }
const SPIN = { base: 620, step: 110, delay: 55 }

const root = ref<HTMLElement | null>(null)
const mode = ref<'idle' | 'intro' | 'spin'>('idle')
let timer: ReturnType<typeof setTimeout> | undefined

function timing(order: number) {
  const t = mode.value === 'intro' ? INTRO : SPIN
  return {
    '--dur': `${t.base + order * t.step}ms`,
    '--delay': `${order * t.delay}ms`,
  }
}

function play(kind: 'intro' | 'spin') {
  if (mode.value !== 'idle' || wheelCount.value === 0) return
  if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return
  mode.value = kind
  const t = kind === 'intro' ? INTRO : SPIN
  const last = wheelCount.value - 1
  timer = setTimeout(() => {
    mode.value = 'idle'
  }, t.base + last * (t.step + t.delay) + 60)
}

/** Roll every wheel one full turn (hover on the tile). */
function spin() {
  play('spin')
}

defineExpose({ spin })

let observer: IntersectionObserver | null = null
onMounted(() => {
  if (!root.value) return
  observer = new IntersectionObserver(([entry]) => {
    if (!entry?.isIntersecting) return
    observer?.disconnect()
    observer = null
    play('intro')
  }, { threshold: 0.6 })
  observer.observe(root.value)
})

onBeforeUnmount(() => {
  observer?.disconnect()
  clearTimeout(timer)
})
</script>

<template>
  <span
    ref="root"
    class="g-odo"
    :class="mode === 'idle' ? null : `is-${mode}`"
  >
    <span class="sr-only">{{ value }}</span>
    <span
      class="g-odo__face"
      aria-hidden="true"
    >
      <template
        v-for="cell in cells"
        :key="cell.key"
      >
        <span
          v-if="cell.digit !== null"
          class="g-odo__cell"
          :style="{ '--d': cell.digit, ...timing(cell.order) }"
        >
          <span class="g-odo__wheel">
            <span
              v-for="n in 30"
              :key="n"
            >{{ (n - 1) % 10 }}</span>
          </span>
        </span>
        <span
          v-else
          class="g-odo__sep"
        >{{ cell.char }}</span>
      </template>
    </span>
  </span>
</template>
