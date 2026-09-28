<script setup lang="ts">
import type { TeamRow } from '~/types/draft'

/**
 * The draft simulator — development only, dropped from builds (nuxt.config).
 * Opened in a browser beside the app (`localhost:3003/#/dev/draft-sim`), it
 * plays a ranked champion select one ban and one pick at a time, in the
 * client's order and against its clock, and sends what the League client would
 * send to the app's shell through the dev server's relay (`/__sim/lcu`): the
 * gameflow phase, the summoner, a mastery list and the champion select session.
 * The app, started with `npm run tauri:sim`, takes it for a real champion
 * select — navigation, parsing and all.
 */
const { sim, steps, current, label, lcuSession, activeSlot, allyLanes, tick } = useDraftSimulator()
const { laneEntries } = useTierList()
const { nameOf } = useChampionStatics()

type Phase = 'Lobby' | 'ChampSelect' | 'InProgress'
const phase = ref<Phase>('Lobby')
const failed = ref<string | null>(null)

const RELAY = '/__sim/lcu'
const event = (uri: string, data: unknown, eventType = 'Update') => ({ kind: 'event', uri, event_type: eventType, data })

async function send(body: { op: 'reset' } | { op: 'push', readings: unknown[] }) {
  try {
    await $fetch(RELAY, { method: 'POST', body })
    failed.value = null
  }
  catch (cause) {
    failed.value = `The relay did not answer: ${String(cause)}`
  }
}

/**
 * A synthetic player, so the app has someone logged in, and a mastery list
 * made of the champions played on our lane, so "My pool" has something to
 * rank. Neither is anyone's real data.
 */
function lobby() {
  const pool = laneEntries(sim.value.myLane).slice(0, 12)
  return [
    event('/lol-summoner/v1/current-summoner', { gameName: 'Simulated', tagLine: 'DEV', summonerLevel: 30, profileIconId: 29 }),
    { kind: 'mastery', data: pool.map((entry, index) => ({ championId: entry.championId, championPoints: 120000 - index * 8000 })) },
  ]
}

async function start() {
  await send({ op: 'reset' })
  await send({ op: 'push', readings: [...lobby(), event('/lol-gameflow/v1/gameflow-phase', 'ChampSelect'), event('/lol-champ-select/v1/session', lcuSession.value)] })
  phase.value = 'ChampSelect'
}

/** The draft ends the way the client ends it: the session is deleted, then the phase moves on. */
async function leave(next: Phase) {
  await send({ op: 'push', readings: [event('/lol-champ-select/v1/session', null, 'Delete'), event('/lol-gameflow/v1/gameflow-phase', next)] })
  phase.value = next
}

// Every change of the draft — a ban, a hover, a lock, a tick of the clock — is a session update, as in the client.
watch(lcuSession, (session) => {
  if (phase.value === 'ChampSelect') void send({ op: 'push', readings: [event('/lol-champ-select/v1/session', session)] })
}, { deep: true })

let clock: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  clock = setInterval(() => {
    if (phase.value === 'ChampSelect') tick()
  }, 1000)
})
onBeforeUnmount(() => clearInterval(clock))

const allyRows = computed<TeamRow[]>(() => sim.value.ally.map((slot, cell) => ({
  championId: slot.championId,
  lane: allyLanes.value[cell]!,
  locked: slot.locked,
  me: cell === sim.value.myCell,
})))
const enemyRows = computed<TeamRow[]>(() => sim.value.enemy.map(slot => ({ championId: slot.championId, lane: null, locked: slot.locked })))
const activeAllyCell = computed(() => (activeSlot.value?.team === 'ally' ? allyRows.value.findIndex(row => row.lane === activeSlot.value!.lane) : null))
const activeEnemyCell = computed(() => (current.value?.kind === 'pick' && current.value.team === 'enemy' ? current.value.cell : null))
const nonNull = (ids: (number | null)[]) => ids.filter((id): id is number => id !== null)

useHead({ title: 'Draft simulator' })
</script>

<template>
  <div class="flex h-screen flex-col bg-default">
    <header class="flex items-center gap-3 border-b border-default px-4 py-3">
      <UIcon name="i-lucide-flask-conical" class="size-5 text-primary" />
      <div class="leading-tight">
        <h1 class="text-sm font-semibold text-highlighted">Draft simulator</h1>
        <p class="text-xs text-muted">Development only. Start the app with <code>npm run tauri:sim</code>; it follows this page as a League client.</p>
      </div>
      <UBadge :color="phase === 'ChampSelect' ? 'primary' : 'neutral'" variant="subtle" class="ml-auto">{{ phase }}</UBadge>
      <UButton v-if="phase !== 'ChampSelect'" label="Start champion select" icon="i-lucide-play" size="sm" @click="start" />
      <template v-else>
        <UButton label="Game starts" icon="i-lucide-swords" color="neutral" variant="subtle" size="sm" @click="leave('InProgress')" />
        <UButton label="Dodge" icon="i-lucide-log-out" color="neutral" variant="ghost" size="sm" @click="leave('Lobby')" />
      </template>
    </header>

    <UAlert v-if="failed" color="error" variant="subtle" :title="failed" class="m-4" />

    <SimulatorBar class="shrink-0 border-b border-default" />

    <div class="flex flex-col gap-4 p-4">
      <DraftTopStrip :ally-bans="nonNull(sim.allyBans)" :enemy-bans="nonNull(sim.enemyBans)" :seconds-left="sim.secondsLeft" :label="label" />
      <div class="grid grid-cols-2 gap-8">
        <DraftTeam team="ally" :rows="allyRows" :active-cell="activeAllyCell" />
        <DraftTeam team="enemy" :rows="enemyRows" :active-cell="activeEnemyCell" />
      </div>
      <p class="text-xs text-dimmed">
        Step {{ Math.min(sim.step + 1, steps.length) }} of {{ steps.length }} ·
        {{ current ? `${current.team === 'ally' ? 'our' : 'their'} ${current.kind}` : 'draft complete' }}
        <template v-if="sim.ally[sim.myCell]?.championId"> · our pick: {{ nameOf(sim.ally[sim.myCell]!.championId!) }}</template>
      </p>
    </div>
  </div>
</template>
