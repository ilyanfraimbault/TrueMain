<script setup lang="ts">
import type { SimRequest, SimSlot, SimTurn } from '~/composables/useLcuSimulator'
import type { Lane } from '~/types/draft'
import type { ChampionPosition } from '#common/utils/positions'
import { LANES, LANE_LABELS, laneIconUrl } from '~/types/draft'

/**
 * The draft simulator — development only, dropped from builds (nuxt.config).
 * Opened in a browser beside the app (`localhost:3003/#/dev/draft-sim`), it is
 * a League client in champion select: click any pick or ban to put a champion
 * there, choose the position we play, hover then lock our pick. Every change
 * is sent as the client would send it — gameflow phase, a synthetic summoner,
 * a mastery list, and the champion select session — to the app's shell,
 * through the dev server's relay (`/__sim/lcu`). The app, started with
 * `npm run tauri:sim`, takes it for a real champion select and answers live.
 */
const { board, taken, championAt, place, lockMine, setMyLane, setTurn, clear, tick, session, answer } = useLcuSimulator()
const { laneEntries } = useTierList()
const { nameOf, portraitOf, champions } = useChampionStatics()

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
 * A synthetic player, so the app has someone logged in, and a mastery list of
 * each lane's most played champions, so "My pool" has something to rank on
 * whichever position is chosen. Neither is anyone's real data.
 */
function lobby() {
  const pool = LANES.flatMap(lane => laneEntries(lane).slice(0, 5).map(entry => entry.championId))
  const mastery = [...new Set(pool)].map((championId, index) => ({ championId, championPoints: 200000 - index * 5000 }))
  return [
    event('/lol-summoner/v1/current-summoner', { gameName: 'Simulated', tagLine: 'DEV', summonerLevel: 30, profileIconId: 29 }),
    { kind: 'mastery', data: mastery },
  ]
}

async function start() {
  await send({ op: 'reset' })
  await send({ op: 'push', readings: [...lobby(), event('/lol-gameflow/v1/gameflow-phase', 'ChampSelect'), event('/lol-champ-select/v1/session', session.value)] })
  phase.value = 'ChampSelect'
}

/** The draft ends the way the client ends it: the session is deleted, then the phase moves on. */
async function leave(next: Phase) {
  await send({ op: 'push', readings: [event('/lol-champ-select/v1/session', null, 'Delete'), event('/lol-gameflow/v1/gameflow-phase', next)] })
  phase.value = next
}

// Every change — a pick, a ban, a hover, a lock, our position, a tick of the clock — is a session update, as in the client.
watch(session, (next) => {
  if (phase.value === 'ChampSelect') void send({ op: 'push', readings: [event('/lol-champ-select/v1/session', next)] })
}, { deep: true })

let clock: ReturnType<typeof setInterval> | undefined
let requests: ReturnType<typeof setInterval> | undefined
onMounted(() => {
  clock = setInterval(() => {
    if (phase.value === 'ChampSelect') tick()
  }, 1000)
  requests = setInterval(answerRequests, 150)
})
onBeforeUnmount(() => {
  clearInterval(clock)
  clearInterval(requests)
})

// ─── The app's requests ─────────────────────────────────────────────────────

/** Answer what the app's shell asked the "client" since the last poll, in order. */
let answering = false
async function answerRequests() {
  if (answering) return
  answering = true
  try {
    const waiting = await $fetch<SimRequest[]>(RELAY, { query: { op: 'requests' } })
    const ids = [...champions.value.keys()]
    for (const request of waiting) {
      const { status, body } = answer(request, ids, phase.value === 'ChampSelect')
      await $fetch(RELAY, { method: 'POST', body: { op: 'answer', id: request.id, status, body } })
    }
  }
  catch {
    // The relay is down with the dev server; the next poll tries again.
  }
  finally {
    answering = false
  }
}

const TURNS: { label: string, value: SimTurn }[] = [
  { label: 'Not ours', value: 'none' },
  { label: 'Our ban', value: 'ban' },
  { label: 'Our pick', value: 'pick' },
]

// ─── Picking ────────────────────────────────────────────────────────────────

const editing = ref<SimSlot | null>(null)
const pickerOpen = computed({
  get: () => editing.value !== null,
  set: (open) => {
    if (!open) editing.value = null
  },
})

const pickerTitle = computed(() => {
  const slot = editing.value
  if (!slot) return ''
  if (slot.kind === 'ban') return slot.team === 'ally' ? 'Our ban' : 'Enemy ban'
  if (slot.kind === 'enemy') return `Enemy ${slot.index + 1}`
  return slot.lane === board.value.myLane ? `Our pick · ${LANE_LABELS[slot.lane]}` : `Ally · ${LANE_LABELS[slot.lane]}`
})

/** A slot clicked starts the champion select if it is not on yet: clicking is all it takes. */
async function edit(slot: SimSlot) {
  if (phase.value !== 'ChampSelect') await start()
  editing.value = slot
}

function onPick(championId: number) {
  if (editing.value) place(editing.value, championId)
}

const mine = computed(() => board.value.allies[board.value.myLane])
</script>

<template>
  <div class="flex min-h-screen flex-col bg-default">
    <header class="flex items-center gap-3 border-b border-default px-5 py-3">
      <UIcon name="i-lucide-flask-conical" class="size-5 text-primary" />
      <div class="leading-tight">
        <h1 class="text-sm font-semibold text-highlighted">Draft simulator</h1>
        <p class="text-xs text-muted">Development only. The app started with <code>npm run tauri:sim</code> follows this page as a League client.</p>
      </div>
      <UBadge :color="phase === 'ChampSelect' ? 'primary' : 'neutral'" variant="subtle" class="ml-auto">{{ phase }}</UBadge>
      <UButton v-if="phase !== 'ChampSelect'" label="Start champion select" icon="i-lucide-play" size="sm" @click="start" />
      <template v-else>
        <UButton label="Game starts" icon="i-lucide-swords" color="neutral" variant="subtle" size="sm" @click="leave('InProgress')" />
        <UButton label="Dodge" icon="i-lucide-log-out" color="neutral" variant="ghost" size="sm" @click="leave('Lobby')" />
      </template>
      <UButton icon="i-lucide-rotate-ccw" label="Clear" color="neutral" variant="ghost" size="sm" @click="clear" />
    </header>

    <UAlert v-if="failed" color="error" variant="subtle" :title="failed" class="mx-5 mt-4" />

    <main class="mx-auto flex w-full max-w-5xl flex-col gap-6 p-5">
      <section class="flex flex-wrap items-center gap-3">
        <span class="stat-label">Our position</span>
        <RolePicker :position="board.myLane" hide-all @update:position="(lane: ChampionPosition | null) => lane && setMyLane(lane as Lane)" />
        <span class="stat-label ml-4">Our turn</span>
        <UFieldGroup size="sm">
          <UButton
            v-for="turn in TURNS"
            :key="turn.value"
            :label="turn.label"
            color="neutral"
            :variant="board.turn === turn.value ? 'solid' : 'subtle'"
            @click="setTurn(turn.value)"
          />
        </UFieldGroup>
        <span class="ml-auto stat-value text-xl tabular-nums">{{ phase === 'ChampSelect' ? board.secondsLeft : '—' }}</span>
      </section>

      <section class="grid grid-cols-2 gap-8">
        <div v-for="team in (['ally', 'enemy'] as const)" :key="team" class="flex flex-col gap-2">
          <h2 class="stat-label">{{ team === 'ally' ? 'Our bans' : 'Enemy bans' }}</h2>
          <div class="flex gap-1.5">
            <button
              v-for="(championId, index) in (team === 'ally' ? board.allyBans : board.enemyBans)"
              :key="index"
              type="button"
              class="group relative flex size-11 items-center justify-center overflow-hidden rounded-md bg-elevated ring-1 ring-default transition hover:ring-accented"
              :title="championId ? `${nameOf(championId)} — click to remove` : 'Add a ban'"
              @click="championId ? place({ kind: 'ban', team, index }, null) : edit({ kind: 'ban', team, index })"
            >
              <img v-if="championId && portraitOf(championId)" :src="portraitOf(championId)!" :alt="nameOf(championId)" class="size-full object-cover grayscale">
              <img
                v-else-if="team === 'ally' && index === LANES.indexOf(board.myLane) && board.myBanHover && portraitOf(board.myBanHover)"
                :src="portraitOf(board.myBanHover)!"
                :alt="`${nameOf(board.myBanHover)} hovered`"
                class="size-full object-cover opacity-50"
              >
              <UIcon v-else name="i-lucide-ban" class="size-4 text-dimmed group-hover:hidden" />
              <UIcon :name="championId ? 'i-lucide-x' : 'i-lucide-plus'" class="absolute hidden size-4 text-highlighted group-hover:block" />
            </button>
          </div>
        </div>
      </section>

      <section class="grid grid-cols-2 gap-8">
        <div class="flex flex-col gap-2">
          <h2 class="stat-label">Our team</h2>
          <div
            v-for="lane in LANES"
            :key="lane"
            class="flex items-center gap-3 rounded-lg p-2"
            :class="lane === board.myLane ? 'bg-accented ring-1 ring-primary/60' : 'bg-elevated'"
          >
            <img :src="laneIconUrl(lane)" :alt="LANE_LABELS[lane]" class="size-5 opacity-80">
            <button type="button" class="flex min-w-0 flex-1 items-center gap-3 rounded-md text-left" @click="edit({ kind: 'ally', lane })">
              <span class="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted ring-1 ring-default">
                <img v-if="championAt({ kind: 'ally', lane }) && portraitOf(championAt({ kind: 'ally', lane })!)" :src="portraitOf(championAt({ kind: 'ally', lane })!)!" alt="" class="size-full object-cover" :class="!board.allies[lane].locked && 'opacity-60'">
                <UIcon v-else name="i-lucide-plus" class="size-4 text-dimmed" />
              </span>
              <span class="min-w-0 leading-tight">
                <span class="block truncate text-sm font-semibold text-highlighted">{{ championAt({ kind: 'ally', lane }) ? nameOf(championAt({ kind: 'ally', lane })!) : (lane === board.myLane ? 'Our pick' : 'Ally') }}</span>
                <span class="text-[11px] text-dimmed">{{ LANE_LABELS[lane] }}{{ lane === board.myLane ? ' · us' : '' }}{{ board.allies[lane].championId && !board.allies[lane].locked ? ' · hovering' : '' }}</span>
              </span>
            </button>
            <UButton v-if="lane === board.myLane && mine.championId && !mine.locked" label="Lock in" icon="i-lucide-lock" size="xs" @click="lockMine" />
            <UButton v-if="board.allies[lane].championId" icon="i-lucide-x" color="neutral" variant="ghost" size="xs" aria-label="Remove" @click="place({ kind: 'ally', lane }, null)" />
          </div>
        </div>

        <div class="flex flex-col gap-2">
          <h2 class="stat-label">Enemy team <span class="normal-case tracking-normal text-dimmed">— lanes unknown, as in the client</span></h2>
          <div v-for="(championId, index) in board.enemies" :key="index" class="flex items-center gap-3 rounded-lg bg-elevated p-2">
            <span class="w-5 text-center text-xs text-dimmed tabular-nums">{{ index + 1 }}</span>
            <button type="button" class="flex min-w-0 flex-1 items-center gap-3 rounded-md text-left" @click="edit({ kind: 'enemy', index })">
              <span class="flex size-10 shrink-0 items-center justify-center overflow-hidden rounded-md bg-muted ring-1 ring-default">
                <img v-if="championId && portraitOf(championId)" :src="portraitOf(championId)!" alt="" class="size-full object-cover">
                <UIcon v-else name="i-lucide-plus" class="size-4 text-dimmed" />
              </span>
              <span class="truncate text-sm font-semibold text-highlighted">{{ championId ? nameOf(championId) : 'Enemy' }}</span>
            </button>
            <UButton v-if="championId" icon="i-lucide-x" color="neutral" variant="ghost" size="xs" aria-label="Remove" @click="place({ kind: 'enemy', index }, null)" />
          </div>
        </div>
      </section>
    </main>

    <SimulatorChampionPicker
      v-model:open="pickerOpen"
      :title="pickerTitle"
      :lane="editing?.kind === 'ally' ? editing.lane : null"
      :exclude="taken"
      @pick="onPick"
    />
  </div>
</template>
