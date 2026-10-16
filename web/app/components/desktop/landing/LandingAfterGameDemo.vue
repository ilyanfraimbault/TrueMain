<script setup lang="ts">
/**
 * After the game, redrawn for `/download` (#2072): the dashboard's goals card
 * filling one slot per game as the games land, and the match's win-probability
 * timeline from the player's side with its turning points. Every value is a
 * sample, and the caption under it says so.
 */
const root = ref<HTMLElement | null>(null)
const { reached } = useSceneBeats(root, [600, 700, 700, 700, 900, 4200])

const CS_RESULTS = [{ value: 7.4, met: true }, { value: 6.6, met: false }, { value: 7.8, met: true }, { value: 7.2, met: true }]
const DEATH_RESULTS = [{ value: 3, met: true }, { value: 4, met: true }, { value: 2, met: true }]

/** The card's two goals and their standing line once `counted` games have landed, worded as the app words them. */
const GOALS: { sentence: string, results: { value: number, met: boolean }[], games: number, standing: (counted: number) => string }[] = [
  {
    sentence: 'CS / min ≥ 7.0 over the next 5 Solo games',
    results: CS_RESULTS,
    games: 5,
    standing: (counted) => {
      const values = CS_RESULTS.slice(0, counted).map(result => result.value)
      if (!values.length) return 'Target 7.0 · 0/5 games'
      return `${(values.reduce((sum, value) => sum + value, 0) / values.length).toFixed(1)} now · target 7.0 · ${values.length}/5 games`
    },
  },
  {
    sentence: 'Deaths ≤ 5 in each of the next 5 Solo games',
    results: DEATH_RESULTS,
    games: 5,
    standing: counted => `${Math.min(counted, DEATH_RESULTS.length)} of 5 met · ${Math.min(counted, DEATH_RESULTS.length)}/5 games`,
  },
]

/** Games landed so far: one per beat from the first, all of them in the final state. */
const counted = computed(() => [1, 2, 3, 4].filter(game => reached(game)).length)

// The match's curve from our side, in 0..1 at each minute of a 31-minute game.
const CURVE = [0.5, 0.5, 0.52, 0.49, 0.46, 0.44, 0.47, 0.53, 0.58, 0.57, 0.55, 0.51, 0.48, 0.5, 0.56, 0.61, 0.6, 0.63, 0.58, 0.54, 0.57, 0.64, 0.7, 0.68, 0.72, 0.79, 0.76, 0.82, 0.88, 0.93, 0.97, 1]
const TURNS = [8, 12, 15, 22, 25]
const W = 320
const H = 96
const point = (value: number, minute: number) => ({ x: (minute / (CURVE.length - 1)) * W, y: (1 - value) * H })
const line = CURVE.map((value, minute) => point(value, minute)).map(({ x, y }) => `${x.toFixed(1)},${y.toFixed(1)}`).join(' ')
const area = `0,${H / 2} ${line} ${W},${H / 2}`
const clip = `landing-wp-${useId()}`
</script>

<template>
  <div ref="root" class="grid gap-4 sm:grid-cols-2">
    <section class="surface flex flex-col gap-3 rounded-lg px-4 py-3">
      <h3 class="text-xs font-semibold uppercase tracking-wide text-muted">Goals</h3>
      <ul class="flex flex-col gap-4">
        <li v-for="goal in GOALS" :key="goal.sentence" class="flex flex-col gap-1.5">
          <p class="text-sm leading-snug text-highlighted">{{ goal.sentence }}</p>
          <div class="flex h-1.5 gap-1">
            <span
              v-for="slot in goal.games"
              :key="slot"
              class="flex-1 rounded-full transition-colors duration-500"
              :class="slot <= Math.min(counted, goal.results.length)
                ? (goal.results[slot - 1]!.met ? 'bg-data-good' : 'bg-data-bad')
                : 'ring-1 ring-inset ring-default'"
            />
          </div>
          <p class="text-[11px] tabular-nums text-muted">{{ goal.standing(counted) }}</p>
        </li>
      </ul>
    </section>

    <section class="surface flex flex-col gap-3 rounded-lg px-4 py-3">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-semibold uppercase tracking-wide text-muted">Win probability</h3>
        <span class="text-[11px] text-dimmed">Victory · 31:12</span>
      </div>
      <svg :viewBox="`0 0 ${W} ${H}`" class="h-auto w-full overflow-visible" aria-hidden="true">
        <defs>
          <clipPath :id="`${clip}-up`"><rect x="0" y="0" :width="W" :height="H / 2" /></clipPath>
          <clipPath :id="`${clip}-down`"><rect x="0" :y="H / 2" :width="W" :height="H / 2" /></clipPath>
        </defs>
        <line x1="0" :y1="H / 2" :x2="W" :y2="H / 2" class="stroke-white/15" stroke-dasharray="3 3" />
        <g class="landing-reveal" :class="{ 'is-on': reached(4) }">
          <polygon :points="area" class="fill-ally/25" :clip-path="`url(#${clip}-up)`" />
          <polygon :points="area" class="fill-enemy/25" :clip-path="`url(#${clip}-down)`" />
          <polyline :points="line" fill="none" class="stroke-(--ui-text-highlighted)" stroke-width="1.5" stroke-linejoin="round" />
          <circle
            v-for="minute in TURNS"
            :key="minute"
            :cx="point(CURVE[minute]!, minute).x"
            :cy="point(CURVE[minute]!, minute).y"
            r="3"
            class="fill-primary stroke-ink-950"
            stroke-width="1.5"
          />
        </g>
      </svg>
      <p class="text-[11px] text-muted">Read from your side, minute by minute, from the client's timeline of the game.</p>
    </section>
  </div>
</template>

<style scoped>
.landing-reveal {
  clip-path: inset(0 100% 0 0);
  transition: clip-path 1400ms cubic-bezier(0.4, 0, 0.2, 1);
}

.landing-reveal.is-on {
  clip-path: inset(0 0 0 0);
}

@media (prefers-reduced-motion: reduce) {
  .landing-reveal { transition: none; }
}
</style>
