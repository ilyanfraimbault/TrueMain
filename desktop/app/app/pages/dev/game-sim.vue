<script setup lang="ts">
import tapeText from '../../../../fixtures/ranked-game.jsonl?raw'

/**
 * The game simulator — development only, dropped from builds (nuxt.config).
 * Opened in a browser beside the app (`localhost:3003/#/dev/game-sim`), it is
 * a game in progress: it plays the committed tape's recorded `allgamedata`
 * readings (`desktop/fixtures/ranked-game.jsonl`) one by one, at a chosen
 * pace or a step at a time, as the game's own API would answer them. Each is
 * sent with the gameflow phase to the app's shell through the dev server's
 * relay (`/__sim/lcu`), like the draft simulator's; the app, started with
 * `npm run tauri:sim`, takes it for a live game — page, feed and all.
 */

interface Line {
  kind: string
  data: unknown
}

interface GameReading {
  gameTime: number
  data: unknown
}

const lines: Line[] = tapeText.split('\n').filter(line => line.trim()).map(line => JSON.parse(line) as Line)
const readings: GameReading[] = lines
  .filter(line => line.kind === 'game')
  .map(line => ({ gameTime: (line.data as { gameData?: { gameTime?: number } }).gameData?.gameTime ?? 0, data: line.data }))
const summoner = lines.find(line => line.kind === 'summoner')?.data ?? null

type Phase = 'Lobby' | 'InProgress' | 'EndOfGame'
const phase = ref<Phase>('Lobby')
/** The index of the last reading sent; -1 before the first. */
const position = ref(-1)
const playing = ref(false)
const failed = ref<string | null>(null)

const PACES = [
  { label: 'Every second', value: 1000 },
  { label: 'Every 3 seconds', value: 3000 },
  { label: 'Every 6 seconds', value: 6000 },
]
const pace = ref(3000)

const RELAY = '/__sim/lcu'
const event = (uri: string, data: unknown) => ({ kind: 'event', uri, event_type: 'Update', data })

async function send(body: { op: 'reset' } | { op: 'push', readings: unknown[] }) {
  try {
    await $fetch(RELAY, { method: 'POST', body })
    failed.value = null
  }
  catch (cause) {
    failed.value = `The relay did not answer: ${String(cause)}`
    playing.value = false
  }
}

/** A new session: the tape's synthetic player, the phase, and the game's first reading. */
async function start() {
  await send({ op: 'reset' })
  await send({ op: 'push', readings: [
    ...(summoner ? [event('/lol-summoner/v1/current-summoner', summoner)] : []),
    event('/lol-gameflow/v1/gameflow-phase', 'InProgress'),
  ] })
  phase.value = 'InProgress'
  position.value = -1
  await goTo(0)
}

/** Send one reading, as the game would answer the next poll. Earlier ones go back in time — items sold, levels lost. */
async function goTo(index: number) {
  const reading = readings[index]
  if (!reading) return
  if (phase.value !== 'InProgress') {
    await start()
    if (index === 0) return
  }
  await send({ op: 'push', readings: [{ kind: 'game', data: reading.data }] })
  position.value = index
}

/** The game ends the way the client ends it: stats, then the end-of-game screen. */
async function end() {
  playing.value = false
  await send({ op: 'push', readings: [
    event('/lol-gameflow/v1/gameflow-phase', 'WaitingForStats'),
    event('/lol-gameflow/v1/gameflow-phase', 'EndOfGame'),
  ] })
  phase.value = 'EndOfGame'
}

let timer: ReturnType<typeof setTimeout> | undefined
function schedule() {
  clearTimeout(timer)
  if (!playing.value) return
  timer = setTimeout(async () => {
    if (position.value >= readings.length - 1) {
      playing.value = false
      return
    }
    await goTo(position.value + 1)
    schedule()
  }, pace.value)
}

async function togglePlay() {
  playing.value = !playing.value
  if (playing.value && phase.value !== 'InProgress') await start()
  schedule()
}

watch(pace, schedule)
onBeforeUnmount(() => clearTimeout(timer))

function minutes(seconds: number) {
  const whole = Math.floor(seconds)
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`
}
</script>

<template>
  <div class="flex min-h-screen flex-col bg-default">
    <header class="flex items-center gap-3 border-b border-default px-5 py-3">
      <UIcon name="i-lucide-flask-conical" class="size-5 text-primary" />
      <div class="leading-tight">
        <h1 class="text-sm font-semibold text-highlighted">Game simulator</h1>
        <p class="text-xs text-muted">Development only. The app started with <code>npm run tauri:sim</code> follows this page as a running game.</p>
      </div>
      <UBadge :color="phase === 'InProgress' ? 'primary' : 'neutral'" variant="subtle" class="ml-auto">{{ phase }}</UBadge>
      <UButton v-if="phase !== 'InProgress'" label="Start game" icon="i-lucide-play" size="sm" @click="start" />
      <template v-else>
        <UButton :label="playing ? 'Pause' : 'Play'" :icon="playing ? 'i-lucide-pause' : 'i-lucide-play'" size="sm" @click="togglePlay" />
        <UButton label="Next reading" icon="i-lucide-skip-forward" color="neutral" variant="subtle" size="sm" :disabled="position >= readings.length - 1" @click="goTo(position + 1)" />
        <UButton label="End game" icon="i-lucide-flag" color="neutral" variant="ghost" size="sm" @click="end" />
      </template>
    </header>

    <UAlert v-if="failed" color="error" variant="subtle" :title="failed" class="mx-5 mt-4" />

    <main class="mx-auto flex w-full max-w-3xl flex-col gap-5 p-5">
      <section class="flex flex-wrap items-center gap-3">
        <span class="stat-label">Tape</span>
        <code class="text-xs text-muted">desktop/fixtures/ranked-game.jsonl</code>
        <span class="text-xs text-dimmed">· {{ readings.length }} readings, synthetic</span>
        <USelect v-model="pace" :items="PACES" size="xs" icon="i-lucide-timer" class="ml-auto w-44" />
      </section>

      <section class="surface overflow-hidden rounded-xl">
        <div class="flex items-center justify-between border-b border-default px-4 py-2">
          <span class="stat-label">Readings</span>
          <span class="text-xs text-dimmed">Click one to send it — an earlier one goes back in time.</span>
        </div>
        <ol class="grid grid-cols-4 gap-1.5 p-3">
          <li v-for="(reading, index) in readings" :key="index">
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-lg px-3 py-2 text-left ring-1 transition"
              :class="index === position ? 'bg-accented ring-primary/60' : index < position ? 'bg-elevated ring-default' : 'ring-default hover:bg-elevated'"
              @click="goTo(index)"
            >
              <span class="w-5 text-xs tabular-nums text-dimmed">{{ index + 1 }}</span>
              <span class="stat-value text-sm tabular-nums" :class="index <= position ? 'text-highlighted' : 'text-muted'">{{ minutes(reading.gameTime) }}</span>
              <UIcon v-if="index === position" name="i-lucide-radio" class="ml-auto size-3.5 text-primary" />
              <UIcon v-else-if="index < position" name="i-lucide-check" class="ml-auto size-3.5 text-dimmed" />
            </button>
          </li>
        </ol>
      </section>
    </main>
  </div>
</template>
