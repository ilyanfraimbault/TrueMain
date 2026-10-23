<script setup lang="ts">
import type { CompositionLane } from '#shared/types/composition'
import type { ChampionStaticListItem } from '#shared/types/static-data'
import { POSITION_OPTIONS, type ChampionPosition } from '#common/utils/positions'

/**
 * The matchup page's draft, laid out like the desktop app's champion select
 * (`DraftScreen`): each team's five picks as tall cards over their lanes, the
 * lane duel between them. It replaces a portrait stage over a card of sixteen
 * select fields: the whole draft now reads at a glance, the way it does in the
 * client, and every slot is picked the same way — a click on its card.
 *
 * The played role decides which two cards are the matchup: the player's own on
 * the ally side, ringed, and the role opponent's across from it, ringed
 * faintly. The eight others only re-weight the similarity search, while the
 * opponent is a hard filter on the sampled games. Before a role is chosen, a
 * card's click picks its lane as the role first — the one thing the board needs
 * to know whose card is whose.
 *
 * Purely presentational: the page owns the draft state, this component only
 * emits picks. One champion search serves every slot.
 */
const props = defineProps<{
  champions: ChampionStaticListItem[]
  playedChampionId: number | null
  playedPosition: ChampionPosition | null
  opponentChampionId: number | null
  allySlots: Record<ChampionPosition, number | null>
  enemySlots: Record<ChampionPosition, number | null>
  /** Enables the clear button — the page decides whether anything is set. */
  hasPicks: boolean
  lane: CompositionLane | null
  laneLoading: boolean
}>()

const emit = defineEmits<{
  'update:playedChampionId': [value: number | null]
  'update:playedPosition': [value: ChampionPosition | null]
  'update:opponentChampionId': [value: number | null]
  'update:ally': [position: ChampionPosition, championId: number | null]
  'update:enemy': [position: ChampionPosition, championId: number | null]
  'clear': []
}>()

const championsById = useChampionsById(() => props.champions)
const championOf = (id: number | null) => (id === null ? null : championsById.value.get(id) ?? null)

type Side = 'ally' | 'enemy'

/** The slot a card or a duel portrait fills: the matchup's two, or a draft slot. */
type Target =
  | { kind: 'played' }
  | { kind: 'opponent' }
  | { kind: Side, position: ChampionPosition }

interface Card {
  position: ChampionPosition
  lane: string
  label: string
  championId: number | null
  mark: 'you' | 'opponent' | null
  target: Target
}

function cardsOf(side: Side): Card[] {
  return POSITION_OPTIONS.map((option) => {
    const matchup = option.value === props.playedPosition
    const lane = option.label.toLowerCase()
    if (matchup) {
      return side === 'ally'
        ? { position: option.value, lane: option.value, label: `Your champion, ${lane}`, championId: props.playedChampionId, mark: 'you' as const, target: { kind: 'played' } as const }
        : { position: option.value, lane: option.value, label: `Role opponent, ${lane}`, championId: props.opponentChampionId, mark: 'opponent' as const, target: { kind: 'opponent' } as const }
    }
    const slots = side === 'ally' ? props.allySlots : props.enemySlots
    return {
      position: option.value,
      lane: option.value,
      label: `${side === 'ally' ? 'Ally' : 'Enemy'} ${lane}`,
      championId: slots[option.value],
      mark: null,
      target: { kind: side, position: option.value },
    }
  })
}

const teams = computed(() => [
  { side: 'ally' as const, title: 'Your team', cards: cardsOf('ally') },
  { side: 'enemy' as const, title: 'Enemy team', cards: cardsOf('enemy') },
])

// ─── The champion search ───────────────────────────────────────────────────

const searchOpen = ref(false)
const target = ref<Target>({ kind: 'played' })

const searchTitle = computed(() => {
  const current = target.value
  if (current.kind === 'played') return 'Choose your champion'
  if (current.kind === 'opponent') return 'Choose the role opponent'
  const lane = POSITION_OPTIONS.find(option => option.value === current.position)?.label.toLowerCase() ?? ''
  return `Choose the ${current.kind === 'ally' ? 'ally' : 'enemy'} ${lane}`
})

const targetChampionId = computed(() => {
  const current = target.value
  if (current.kind === 'played') return props.playedChampionId
  if (current.kind === 'opponent') return props.opponentChampionId
  return (current.kind === 'ally' ? props.allySlots : props.enemySlots)[current.position]
})

function search(next: Target) {
  target.value = next
  searchOpen.value = true
}

/**
 * A card's click. With no role yet, its lane becomes the role and the card
 * becomes the matchup's — ours on the ally side, the opponent's across — so
 * the search fills the slot the card now stands for.
 */
function pickCard(side: Side, card: Card) {
  if (props.playedPosition === null) {
    emit('update:playedPosition', card.position)
    search(side === 'ally' ? { kind: 'played' } : { kind: 'opponent' })
    return
  }
  search(card.target)
}

function select(championId: number | null) {
  const current = target.value
  if (current.kind === 'played') emit('update:playedChampionId', championId)
  else if (current.kind === 'opponent') emit('update:opponentChampionId', championId)
  else if (current.kind === 'ally') emit('update:ally', current.position, championId)
  else emit('update:enemy', current.position, championId)
}
</script>

<template>
  <section
    class="grid gap-x-4 gap-y-6 lg:grid-cols-[minmax(0,1fr)_12rem_minmax(0,1fr)]"
    aria-label="Draft"
  >
    <template
      v-for="(team, index) in teams"
      :key="team.side"
    >
      <!-- The duel sits between the two teams on wide screens and between
           them in the stack on narrow ones: the grid's order puts it there. -->
      <BuilderLaneDuel
        v-if="index === 1"
        class="self-center"
        :position="playedPosition"
        :champion="championOf(playedChampionId)"
        :opponent="championOf(opponentChampionId)"
        :lane="lane"
        :loading="laneLoading"
        @update:position="$emit('update:playedPosition', $event)"
        @pick-champion="search({ kind: 'played' })"
        @pick-opponent="search({ kind: 'opponent' })"
      />

      <div class="mx-auto w-full max-w-lg space-y-2 lg:max-w-none">
        <div class="flex h-6 items-center gap-2">
          <h2 class="stat-label">
            {{ team.title }}
          </h2>
          <UButton
            v-if="team.side === 'enemy' && hasPicks"
            class="ms-auto"
            variant="ghost"
            color="neutral"
            size="xs"
            icon="i-lucide-eraser"
            @click="$emit('clear')"
          >
            Clear draft
          </UButton>
        </div>
        <ul class="grid grid-cols-5 gap-1.5">
          <li
            v-for="card in team.cards"
            :key="card.position"
          >
            <BuilderDraftCard
              :champion="championOf(card.championId)"
              :side="team.side"
              :lane="card.lane"
              :mark="card.mark"
              :label="card.label"
              @click="pickCard(team.side, card)"
            />
          </li>
        </ul>
      </div>
    </template>

    <BuilderChampionSearch
      v-model:open="searchOpen"
      :champions="champions"
      :champion-id="targetChampionId"
      :title="searchTitle"
      @select="select"
    />
  </section>
</template>
