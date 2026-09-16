<script setup lang="ts">
import type { MatchDetailParticipant } from '~~/shared/types/match-detail'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'
import { formatPercentage } from '~~/shared/utils/ddragon'

const props = defineProps<{
  participants: MatchDetailParticipant[]
  teamId: number
  win: boolean
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse
}>()

function champ(id: number) {
  return props.champions.find(c => c.championId === id) ?? null
}

function champName(id: number) {
  return champ(id)?.name ?? `Champion ${id}`
}

// Profile slug for a tracked participant — only when BOTH gameName and tagLine
// are present. A null tagLine would otherwise produce a trailing-dash slug
// (`Name-`) that NameTagParser can't resolve, yielding a 404 link.
function profileSlug(p: MatchDetailParticipant): string | null {
  if (!p.gameName || !p.tagLine) return null
  return `/truemains/${encodeURIComponent(`${p.gameName}-${p.tagLine}`)}`
}

// Highest damage across the rendered team — drives the damage bar fill, and
// the one participant who reaches it gets the full-strength bar.
const maxDamage = computed(() =>
  props.participants.reduce((max, p) => Math.max(max, p.totalDamageDealtToChampions), 0),
)

function damagePct(value: number) {
  return maxDamage.value > 0 ? Math.round((value / maxDamage.value) * 100) : 0
}

function kda(p: MatchDetailParticipant) {
  if (p.deaths === 0) return 'Perfect'
  return ((p.kills + p.assists) / p.deaths).toFixed(2)
}

// `11697` → `11.7k`: the bar already carries the comparison, the label only
// has to give the order of magnitude, and a fixed one-decimal form keeps the
// five labels the same width.
function fmtDamage(value: number) {
  return value >= 1000 ? `${(value / 1000).toFixed(1)}k` : `${value}`
}

const sideLabel = computed(() => (props.teamId === 100 ? 'Blue' : 'Red'))

// Team aggregates for the header — real sums over the rendered side, no
// fabricated objective/score data.
const teamKda = computed(() => props.participants.reduce(
  (acc, p) => {
    acc.k += p.kills
    acc.d += p.deaths
    acc.a += p.assists
    return acc
  },
  { k: 0, d: 0, a: 0 },
))
const teamGold = computed(() => props.participants.reduce((sum, p) => sum + p.goldEarned, 0))

function fmtGold(value: number) {
  return value >= 1000 ? `${(value / 1000).toFixed(1)}k` : `${value}`
}

// ── Performance score (#639) ─────────────────────────────────────────────
// `performanceScore` / `placement` / `isMvp` / `isAce` all come from the API —
// nothing here is computed or invented client-side, the component only picks
// the presentation.

// Four-step scale inside the brand rose-gold (`primary`) palette: the better
// the game, the denser the pill. Sub-50 stays neutral surface so an average row
// doesn't shout.
function scoreClass(score: number) {
  if (score >= 80) return 'bg-primary/20 text-primary ring-primary/50'
  if (score >= 65) return 'bg-primary/12 text-primary ring-primary/30'
  if (score >= 50) return 'bg-primary/8 text-primary ring-primary/25'
  return 'bg-default/50 text-muted ring-default'
}

// 1 → 1st, 2 → 2nd, 3 → 3rd, 11..13 → 11th..13th.
function ordinal(placement: number) {
  const mod100 = placement % 100
  if (mod100 >= 11 && mod100 <= 13) return `${placement}th`
  switch (placement % 10) {
    case 1: return `${placement}st`
    case 2: return `${placement}nd`
    case 3: return `${placement}rd`
    default: return `${placement}th`
  }
}
</script>

<template>
  <section class="surface overflow-hidden rounded-md">
    <header
      class="flex items-center justify-between px-3 py-2 text-xs font-semibold"
      :class="win ? 'text-sky-400' : 'text-red-400'"
    >
      <span>{{ sideLabel }} team — {{ win ? 'Victory' : 'Defeat' }}</span>
      <span class="flex items-center gap-2 text-muted tabular-nums">
        <span>
          {{ teamKda.k }}<span class="text-muted/60">/</span>{{ teamKda.d }}<span class="text-muted/60">/</span>{{ teamKda.a }}
        </span>
        <span class="text-muted/50">·</span>
        <span class="inline-flex items-center gap-1">
          <UIcon name="i-lucide-coins" class="size-3 text-amber-400/80" />
          {{ fmtGold(teamGold) }}
        </span>
      </span>
    </header>

    <ul class="divide-y divide-default/40">
      <li
        v-for="p in participants"
        :key="p.participantId"
        class="flex items-center gap-2 px-3 py-2"
      >
        <!-- Champion + level -->
        <div class="relative shrink-0">
          <ChampionLink
            :champion-id="p.championId"
            :name="champName(p.championId)"
            :icon-url="champ(p.championId)?.iconUrl"
            class="block size-10 overflow-hidden rounded"
          />
          <span
            class="absolute -bottom-1 -right-1 inline-flex size-4 items-center justify-center rounded-full bg-default text-[10px] font-bold leading-none ring-1 ring-default"
          >
            {{ p.champLevel }}
          </span>
        </div>

        <!-- Summoner spells -->
        <div class="flex shrink-0 flex-col gap-0.5">
          <GameTooltipSummonerSpellIcon
            :spell="summonerSpells[p.summoner1Id] ?? null"
            :width="18"
            :height="18"
            class="size-[18px] rounded"
          />
          <GameTooltipSummonerSpellIcon
            :spell="summonerSpells[p.summoner2Id] ?? null"
            :width="18"
            :height="18"
            class="size-[18px] rounded"
          />
        </div>

        <!-- Keystone + secondary tree -->
        <div class="flex shrink-0 flex-col items-center gap-0.5">
          <GameTooltipPerkIcon
            :perk="p.keystoneId ? runeTree.perks[p.keystoneId] ?? null : null"
            :width="18"
            :height="18"
            class="size-[18px] rounded-full bg-black/40"
          />
          <GameTooltipPerkStyleIcon
            :style="p.subStyleId ? runeTree.perkStyles[p.subStyleId] ?? null : null"
            :width="14"
            :height="14"
            class="size-[14px]"
          />
        </div>

        <!-- Rank crest + identity. The crest alone names the tier; the full
             rank and LP sit in its tooltip, the same card the leaderboard's
             rank emblem opens. A fixed slot (empty when unranked) keeps the
             names aligned down the list. -->
        <div class="flex min-w-0 flex-[1.2] items-center gap-1.5">
          <UTooltip
            v-if="p.rank"
            :delay-duration="150"
            :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
          >
            <span class="inline-flex size-5 shrink-0 items-center justify-center">
              <RankIcon :tier="p.rank.tier" :size="20" loading="lazy" />
            </span>
            <template #content>
              <GameTooltipSurface>
                <RankSummary
                  :tier="p.rank.tier"
                  :division="p.rank.division"
                  :league-points="p.rank.leaguePoints"
                  :size="32"
                />
              </GameTooltipSurface>
            </template>
          </UTooltip>
          <span v-else class="size-5 shrink-0" aria-hidden="true" />
          <NuxtLink
            v-if="profileSlug(p)"
            :to="profileSlug(p)!"
            class="truncate text-xs font-medium text-default transition-colors hover:text-primary"
          >
            {{ p.gameName }}
          </NuxtLink>
          <span v-else class="truncate text-xs font-medium text-muted">{{ p.gameName ?? p.summonerName }}</span>
        </div>

        <!-- KDA -->
        <div class="flex w-[4.5rem] shrink-0 flex-col items-center text-xs">
          <span class="font-semibold tabular-nums">
            {{ p.kills }}<span class="text-muted/70">/</span><span class="text-red-400">{{ p.deaths }}</span><span class="text-muted/70">/</span>{{ p.assists }}
          </span>
          <span class="text-[10px] text-muted tabular-nums">{{ kda(p) }} KDA</span>
        </div>

        <!-- CS + KP -->
        <div class="flex w-[3.5rem] shrink-0 flex-col items-center text-[10px] text-muted tabular-nums">
          <span>{{ p.cs }} CS</span>
          <span>{{ formatPercentage(p.killParticipation, 0) }} KP</span>
        </div>

        <!-- Damage: bar first, then the figure and its per-minute rate under
             it. The team's top dealer gets the full-strength fill so the
             carry reads at a glance; everyone else sits on a softer tone. -->
        <div class="hidden w-[6.5rem] shrink-0 flex-col gap-1 sm:flex">
          <div class="h-1.5 w-full overflow-hidden rounded-full bg-white/8">
            <div
              class="h-full rounded-full transition-[width]"
              :class="p.totalDamageDealtToChampions === maxDamage
                ? 'bg-primary shadow-[0_0_8px] shadow-primary/60'
                : 'bg-primary/45'"
              :style="{ width: `${damagePct(p.totalDamageDealtToChampions)}%` }"
            />
          </div>
          <span class="whitespace-nowrap text-[11px] leading-none tabular-nums">
            <span class="font-semibold text-default">{{ fmtDamage(p.totalDamageDealtToChampions) }}</span>
            <span class="text-muted"> ({{ Math.round(p.damagePerMin) }}/m)</span>
          </span>
        </div>

        <!--
          Right cluster: items then the performance score, edge-aligned with a
          single `ml-auto` on the wrapper so the score stays flush right even at
          the breakpoints where the items block is hidden.
        -->
        <div class="ml-auto flex shrink-0 items-center gap-2">
          <!-- Items: the same grid + trinket/boots column as the collapsed row. -->
          <div class="hidden md:block">
            <MatchItemGrid
              :item-ids="p.items"
              :trinket-item-id="p.trinketItemId"
              :items="items"
              :size="20"
            />
          </div>

          <!--
            Performance score + placement. MVP = crown (best of the winning side)
            in the brand's single genuine-gold accent; ACE = rosette (best of the
            losing side) in the rose `primary`. Both are palette tokens, so the
            two accolades stay distinguishable *and* track the theme — `gold` is
            declared alongside `rosegold` in main.css for exactly this
            "rose GOLD" read, and is the closest token to the crown's meaning.
          -->
          <div class="flex w-[3.25rem] shrink-0 flex-col items-end gap-0.5">
            <UTooltip :text="`Performance score ${p.performanceScore}/100`">
              <span
                class="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[11px] font-bold leading-none tabular-nums ring-1"
                :class="scoreClass(p.performanceScore)"
              >
                <UIcon
                  v-if="p.isMvp || p.isAce"
                  :name="p.isMvp ? 'i-lucide-crown' : 'i-lucide-award'"
                  class="size-3"
                  :class="p.isMvp ? 'text-gold' : 'text-primary'"
                  :aria-label="p.isMvp ? 'MVP' : 'ACE'"
                />
                {{ p.performanceScore }}
              </span>
            </UTooltip>
            <span class="text-[10px] leading-none text-muted tabular-nums">
              {{ ordinal(p.placement) }}
            </span>
          </div>
        </div>
      </li>
    </ul>
  </section>
</template>
