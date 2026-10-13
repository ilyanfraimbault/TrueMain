<script setup lang="ts">
import type { MatchDetailParticipant } from '#shared/types/match-detail'
import { truemainNameTag, truemainProfilePath } from '#shared/utils/truemain-path'

// A scoreboard player's name, with the rank crest under it. The crest alone
// names the tier; the full rank and LP sit in its tooltip, the same card the
// leaderboard's rank emblem opens.
const props = defineProps<{
  participant: MatchDetailParticipant
}>()

// Profile link for a tracked participant — only when BOTH gameName and tagLine
// are present. A null tagLine would otherwise produce a trailing-dash slug
// (`Name-`) that NameTagParser can't resolve, yielding a 404 link.
const profilePath = computed(() => {
  const { gameName, tagLine } = props.participant
  return gameName && tagLine ? truemainProfilePath(truemainNameTag(gameName, tagLine)) : null
})
</script>

<template>
  <div class="flex min-w-0 flex-col items-start gap-0.5">
    <NuxtLink
      v-if="profilePath"
      :to="profilePath"
      class="max-w-full truncate text-xs font-medium text-default transition-colors hover:text-primary"
    >
      {{ participant.gameName }}
    </NuxtLink>
    <span v-else class="max-w-full truncate text-xs font-medium text-muted">{{ participant.gameName ?? participant.summonerName }}</span>
    <UTooltip
      v-if="participant.rank"
      :delay-duration="150"
      :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
    >
      <span class="inline-flex size-[18px] items-center justify-center">
        <RankIcon :tier="participant.rank.tier" :size="18" loading="lazy" />
      </span>
      <template #content>
        <GameTooltipSurface>
          <RankSummary
            :tier="participant.rank.tier"
            :division="participant.rank.division"
            :league-points="participant.rank.leaguePoints"
            :size="32"
          />
        </GameTooltipSurface>
      </template>
    </UTooltip>
  </div>
</template>
