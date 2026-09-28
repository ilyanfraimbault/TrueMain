<script setup lang="ts">
import type { Lane } from '~/types/draft'
import type { BoardSlot } from '~/composables/useDraftBoard'
import type { ChampionPosition } from '~/utils/positions'
import { LANE_LABELS } from '~/types/draft'

/**
 * Champion select — live, or planned by hand, one page. While the client is in
 * champion select this is the live draft, and the gameflow phase opens it on
 * its own (`app.vue`). The rest of the time it is the same draft screen over a
 * board the player fills in any order — their lane, both sides' picks, the
 * bans — with no turns and no timer to act out; a real champion select takes
 * the page over the moment one starts.
 */
const { state, screen } = useLcuState()
const live = computed(() => screen.value === 'draft' && state.value.draft !== null)

const { board, draft, enemyLanes, placed, isEmpty, place, setMyLane, clear } = useDraftBoard()

/** The slot the champion picker is filling, while it is open. */
const editing = ref<BoardSlot | null>(null)
const pickerOpen = computed({
  get: () => editing.value !== null,
  set: (open) => {
    if (!open) editing.value = null
  },
})

const pickerTitle = computed(() => {
  const slot = editing.value
  if (!slot) return ''
  if (slot.kind === 'ban') return slot.team === 'ally' ? 'Your team bans' : 'The enemy bans'
  const who = slot.team === 'enemy' ? 'Enemy' : slot.lane === board.value.myLane ? 'Your pick' : 'Ally'
  return `${who} · ${LANE_LABELS[slot.lane]}`
})

function onPick(championId: number) {
  if (editing.value) place(editing.value, championId)
}

/** A ban goes in the side's first free slot; the strip has no order to keep. */
function addBan(team: 'ally' | 'enemy') {
  const index = (team === 'ally' ? board.value.allyBans : board.value.enemyBans).indexOf(null)
  if (index >= 0) editing.value = { kind: 'ban', team, index }
}

function removeBan(championId: number) {
  for (const team of ['ally', 'enemy'] as const) {
    const index = (team === 'ally' ? board.value.allyBans : board.value.enemyBans).indexOf(championId)
    if (index >= 0) place({ kind: 'ban', team, index }, null)
  }
}
</script>

<template>
  <DraftScreen v-if="live && state.draft" :draft="state.draft" />

  <div v-else class="h-full">
    <DraftScreen
      :draft="draft"
      :placed-enemies="enemyLanes"
      editable
      class="h-full"
      @place="(team, lane) => (editing = { kind: 'pick', team, lane })"
      @remove="(team, lane) => place({ kind: 'pick', team, lane }, null)"
      @ban="addBan"
      @unban="removeBan"
      @pick="place({ kind: 'pick', team: 'ally', lane: board.myLane }, $event)"
    >
      <template #strip-center>
        <div class="flex flex-col items-center gap-1">
          <span class="stat-label">Your lane</span>
          <div class="flex items-center gap-1">
                <RolePicker
                :position="board.myLane"
                hide-all
                @update:position="(lane: ChampionPosition | null) => lane && setMyLane(lane as Lane)"
              />
            <UTooltip text="Clear the board">
              <UButton icon="i-lucide-rotate-ccw" color="neutral" variant="ghost" size="sm" aria-label="Clear the board" :disabled="isEmpty" @click="clear" />
            </UTooltip>
          </div>
        </div>
      </template>
    </DraftScreen>

    <DraftChampionPicker
      v-model:open="pickerOpen"
      :title="pickerTitle"
      :lane="editing?.kind === 'pick' ? editing.lane : null"
      :exclude="placed"
      @pick="onPick"
    />
  </div>
</template>
