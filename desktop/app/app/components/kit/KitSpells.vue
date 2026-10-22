<script setup lang="ts">
import type { StaticSummonerSpellData } from '#shared/types/static-data'
import { RIFT_SPELLS } from '~/utils/kit-items'

/**
 * A kit's two summoner spells, each slot opening the Rift's spells. Taking the
 * spell the other slot holds swaps the two, as the client does, so the pair
 * never holds one spell twice.
 */
const props = defineProps<{ summoners: Record<number, StaticSummonerSpellData> }>()
const spells = defineModel<[number, number]>({ required: true })

const open = ref<[boolean, boolean]>([false, false])
const name = (id: number) => props.summoners[id]?.name ?? `Spell ${id}`

function choose(slot: 0 | 1, spellId: number) {
  const other = slot === 0 ? 1 : 0
  const next: [number, number] = [...spells.value]
  if (next[other] === spellId) next[other] = next[slot]
  next[slot] = spellId
  spells.value = next
  open.value[slot] = false
}
</script>

<template>
  <div class="flex gap-1.5">
    <UPopover v-for="slot in [0, 1] as const" :key="slot" v-model:open="open[slot]">
      <button
        type="button"
        class="flex rounded transition hover:brightness-125 focus-visible:outline-2 focus-visible:outline-primary"
        :aria-label="`Spell ${slot + 1}: ${name(spells[slot])}`"
      >
        <GameTooltipSummonerSpellIcon :spell="summoners[spells[slot]] ?? null" :width="36" :height="36" class="size-9 rounded" />
      </button>
      <template #content>
        <div class="grid grid-cols-5 gap-1.5 p-2" role="group" :aria-label="`Spell ${slot + 1}`">
          <button
            v-for="spellId in RIFT_SPELLS"
            :key="spellId"
            type="button"
            :aria-pressed="spells[slot] === spellId"
            :aria-label="name(spellId)"
            :title="name(spellId)"
            class="flex rounded transition"
            :class="spells[slot] === spellId ? 'selected-perk' : 'opacity-80 hover:opacity-100'"
            @click="choose(slot, spellId)"
          >
            <img :src="summoners[spellId]?.iconUrl" :alt="name(spellId)" width="32" height="32" class="size-8 rounded img-skeleton">
          </button>
        </div>
      </template>
    </UPopover>
  </div>
</template>
