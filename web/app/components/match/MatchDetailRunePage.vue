<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type { ChampionStaticListItem, RuneTreeResponse } from '~~/shared/types/static-data'

const props = defineProps<{
  participant: MatchDetailParticipant
  champions: ChampionStaticListItem[]
  runeTree: RuneTreeResponse
}>()

const champ = computed(() =>
  props.champions.find(c => c.championId === props.participant.championId) ?? null,
)
const champName = computed(() => champ.value?.name ?? `Champion ${props.participant.championId}`)

// Split the 6-rune page into primary tree (the keystone's style) and the
// secondary tree, preserving the catalog order the API sent.
const primaryRunes = computed(() =>
  props.participant.runes.filter(r => r.styleId === props.participant.primaryStyleId),
)
const secondaryRunes = computed(() =>
  props.participant.runes.filter(r => r.styleId === props.participant.subStyleId),
)

const primaryStyle = computed(() => props.runeTree.perkStyles[props.participant.primaryStyleId] ?? null)
const secondaryStyle = computed(() => props.runeTree.perkStyles[props.participant.subStyleId] ?? null)

const statShards = computed(() => [
  props.participant.statPerkOffense,
  props.participant.statPerkFlex,
  props.participant.statPerkDefense,
].filter(id => id > 0))

function perk(id: number) {
  return props.runeTree.perks[id] ?? null
}
</script>

<template>
  <section class="glass rounded-md border border-default/60 bg-elevated/40 p-3">
    <header class="mb-3 flex items-center gap-2">
      <ChampionLink
        :champion-id="participant.championId"
        :name="champName"
        :icon-url="champ?.iconUrl"
        class="block size-9 overflow-hidden rounded"
      />
      <p class="truncate text-xs font-semibold text-default">
        {{ participant.gameName ?? participant.summonerName }}
      </p>
    </header>

    <div class="grid grid-cols-2 gap-3">
      <!-- Primary tree -->
      <div>
        <div class="mb-2 flex items-center gap-1.5">
          <GameTooltipPerkStyleIcon
            :style="primaryStyle"
            :width="18"
            :height="18"
            class="size-[18px]"
          />
          <span class="text-[10px] font-semibold uppercase tracking-wide text-muted">
            {{ primaryStyle?.name ?? 'Primary' }}
          </span>
        </div>
        <div class="flex flex-wrap gap-1.5">
          <GameTooltipPerkIcon
            v-for="(rune, idx) in primaryRunes"
            :key="`p-${idx}`"
            :perk="perk(rune.perkId)"
            :width="rune.selectionIndex === 0 ? 32 : 24"
            :height="rune.selectionIndex === 0 ? 32 : 24"
            class="rounded-full bg-black/40"
            :class="rune.selectionIndex === 0 ? 'size-8 ring-1 ring-primary/50' : 'size-6'"
          />
        </div>
      </div>

      <!-- Secondary tree + stat shards -->
      <div>
        <div class="mb-2 flex items-center gap-1.5">
          <GameTooltipPerkStyleIcon
            :style="secondaryStyle"
            :width="18"
            :height="18"
            class="size-[18px]"
          />
          <span class="text-[10px] font-semibold uppercase tracking-wide text-muted">
            {{ secondaryStyle?.name ?? 'Secondary' }}
          </span>
        </div>
        <div class="flex flex-wrap gap-1.5">
          <GameTooltipPerkIcon
            v-for="(rune, idx) in secondaryRunes"
            :key="`s-${idx}`"
            :perk="perk(rune.perkId)"
            :width="24"
            :height="24"
            class="size-6 rounded-full bg-black/40"
          />
        </div>

        <div v-if="statShards.length" class="mt-3">
          <p class="mb-1 text-[10px] font-semibold uppercase tracking-wide text-muted">Shards</p>
          <div class="flex gap-1.5">
            <GameTooltipPerkIcon
              v-for="(shard, idx) in statShards"
              :key="`shard-${idx}`"
              :perk="perk(shard)"
              :width="18"
              :height="18"
              class="size-[18px] rounded-full bg-black/40"
            />
          </div>
        </div>
      </div>
    </div>
  </section>
</template>
