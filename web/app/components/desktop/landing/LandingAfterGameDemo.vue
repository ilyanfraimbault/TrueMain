<script setup lang="ts">
/**
 * After the game, redrawn for `/download` (#2072): the dashboard's Today card
 * and goals card (`DashboardTodayCard.vue`, `DashboardGoalsCard.vue`) as four
 * sample games land one by one — the day's record and LP move, each goal's
 * slots fill. Every value is a sample.
 */
const root = ref<HTMLElement | null>(null)
const { reached } = useSceneBeats(root, [400, 700, 700, 700, 4200])

/** The sample's four games of the day, in order. */
const GAMES = [
  { win: true, lp: 22, seconds: 1860, cs: 7.4, deaths: 3 },
  { win: false, lp: -18, seconds: 1690, cs: 6.6, deaths: 4 },
  { win: true, lp: 21, seconds: 2040, cs: 7.8, deaths: 2 },
  { win: true, lp: 20, seconds: 1580, cs: 7.2, deaths: 5 },
]

/** Games landed so far: one per beat from the first, all of them in the final state. */
const counted = computed(() => [1, 2, 3, 4].filter(game => reached(game)).length)

/** The Today card once `counted` games have landed, its play time worded as `formatPlayTime` words it. */
const today = computed(() => {
  const games = GAMES.slice(0, counted.value)
  const minutes = Math.floor(games.reduce((sum, game) => sum + game.seconds, 0) / 60)
  return {
    games: games.length,
    wins: games.filter(game => game.win).length,
    losses: games.filter(game => !game.win).length,
    lp: games.reduce((sum, game) => sum + game.lp, 0),
    time: minutes >= 60 ? `${Math.floor(minutes / 60)}h ${String(minutes % 60).padStart(2, '0')}m` : `${minutes}m`,
  }
})

/** The card's two goals and their standing line once `counted` games have landed, worded as the app words them. */
const GOALS: { sentence: string, results: boolean[], games: number, standing: (counted: number) => string }[] = [
  {
    sentence: 'CS / min ≥ 7.0 over the next 5 Solo games',
    results: GAMES.map(game => game.cs >= 7),
    games: 5,
    standing: (counted) => {
      const values = GAMES.slice(0, counted).map(game => game.cs)
      if (!values.length) return 'Target 7.0 · 0/5 games'
      return `${(values.reduce((sum, value) => sum + value, 0) / values.length).toFixed(1)} now · target 7.0 · ${values.length}/5 games`
    },
  },
  {
    sentence: 'Deaths ≤ 5 in each of the next 5 Solo games',
    results: GAMES.map(game => game.deaths <= 5),
    games: 5,
    standing: counted => `${counted} of 5 met · ${counted}/5 games`,
  },
]
</script>

<template>
  <div ref="root" class="grid items-start gap-4 sm:grid-cols-2">
    <section class="surface flex flex-col gap-2 rounded-lg px-4 py-3">
      <div class="flex items-baseline justify-between gap-2">
        <h3 class="text-xs font-semibold uppercase tracking-wide text-muted">Today</h3>
        <span class="text-xs tabular-nums text-muted">{{ today.games }} game{{ today.games === 1 ? '' : 's' }} · {{ today.time }}</span>
      </div>
      <div class="flex items-baseline justify-between gap-2 tabular-nums">
        <p class="text-lg font-semibold leading-none text-highlighted">{{ today.wins }}W – {{ today.losses }}L</p>
        <p class="flex items-center gap-1.5 text-sm font-semibold text-default">
          <UIcon
            :name="today.lp >= 0 ? 'i-lucide-trending-up' : 'i-lucide-trending-down'"
            class="size-3.5 shrink-0"
            :class="today.lp >= 0 ? 'text-data-good' : 'text-data-bad'"
          />
          {{ Math.abs(today.lp) }} LP
        </p>
      </div>
    </section>

    <section class="surface flex flex-col gap-3 rounded-lg px-4 py-3">
      <div class="flex items-center justify-between">
        <h3 class="text-xs font-semibold uppercase tracking-wide text-muted">Goals</h3>
        <span class="-my-1 p-1 text-muted"><UIcon name="i-lucide-plus" class="block size-4" /></span>
      </div>
      <ul class="flex flex-col gap-3">
        <li v-for="goal in GOALS" :key="goal.sentence" class="flex flex-col gap-1.5">
          <div class="flex items-start gap-2">
            <p class="min-w-0 flex-1 text-sm leading-snug text-highlighted">{{ goal.sentence }}</p>
            <span class="-my-1 p-1 text-muted"><UIcon name="i-lucide-ellipsis" class="block size-4" /></span>
          </div>
          <div class="flex h-1.5 gap-1">
            <span
              v-for="slot in goal.games"
              :key="slot"
              class="flex-1 rounded-full transition-colors duration-500"
              :class="slot <= counted ? (goal.results[slot - 1] ? 'bg-data-good' : 'bg-data-bad') : 'ring-1 ring-inset ring-default'"
            />
          </div>
          <p class="text-[11px] tabular-nums text-muted">{{ goal.standing(counted) }}</p>
        </li>
      </ul>
    </section>
  </div>
</template>
