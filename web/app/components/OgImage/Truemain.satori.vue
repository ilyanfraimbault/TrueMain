<script setup lang="ts">
import type { TruemainOgCard } from '~~/shared/types/og-card'
import { formatPercentage } from '~~/shared/utils/ddragon'
import { formatDedicationScore } from '~/utils/dedication'
import {
  BACKDROP,
  formatCount,
  formatRank,
  GOLD_HAIRLINE,
  INK,
  PANEL,
  PANEL_BORDER,
  RANK_COLOR,
  ROSEGOLD,
  ROSEGOLD_LIGHT,
  TEXT,
  TEXT_DIM,
  TEXT_MUTED,
} from './theme'

/**
 * Share card for `/truemains/{nameTag}` (#926).
 *
 * Like the champion card, the only prop is the identifier the page had at SSR
 * time; the profile is resolved at render time by `/api/og/truemain/{nameTag}`.
 *
 * Fallback contract, in one place:
 *   - unknown player → the plain branded card (the page renders "not found" for
 *     the same input, so a profile-shaped preview would be a lie);
 *   - no ranked snapshot → the rank line reads "Unranked", which is the real
 *     answer — it is never a 0 LP or an invented tier;
 *   - `wins`/`losses` absent from Riot's league response → the W-L line and the
 *     win-rate tile are both dropped, not zeroed;
 *   - no classified main → the champion block and its tiles disappear; the card
 *     still carries the identity and the rank it does know;
 *   - portraits are `background-image`, so a DDragon failure degrades to an
 *     empty plate rather than failing the render.
 */
const props = defineProps<{
  /** The `{gameName}-{tagLine}` profile slug, straight off the route. */
  nameTag?: string
}>()

// Never throw: a crawler that gets an error shows no preview at all, which is
// worse than the branded card.
const card = await (async (): Promise<TruemainOgCard | null> => {
  const nameTag = props.nameTag?.trim()
  if (!nameTag) return null
  return $fetch<TruemainOgCard>(`/api/og/truemain/${encodeURIComponent(nameTag)}`)
    .catch(() => null)
})()

const riotId = computed(() => card?.riotId ?? null)
const ranked = computed(() => card?.ranked ?? null)
const main = computed(() => card?.main ?? null)

const rankColor = computed(() =>
  ranked.value ? RANK_COLOR[ranked.value.tier.toUpperCase()] ?? TEXT : TEXT_MUTED,
)
/** "DIAMOND II · 42 LP", or the honest "Unranked" when there is no snapshot. */
const rankLine = computed(() => {
  const r = ranked.value
  if (!r) return 'Unranked'
  return `${formatRank(r.tier, r.division)} · ${formatCount(r.leaguePoints)} LP`
})
/** Only rendered when Riot returned both halves of the record. */
const recordLine = computed(() => {
  const r = ranked.value
  if (!r || r.wins === null || r.losses === null) return null
  return `${formatCount(r.wins)}W ${formatCount(r.losses)}L`
})

/**
 * Assembled rather than templated so an unknown stat is a missing tile, not a
 * rendered placeholder. Capped at three so the row never wraps.
 */
const tiles = computed(() => {
  const entries: Array<{ label: string, value: string, accent: boolean }> = []
  if (card?.dedicationScore !== null && card?.dedicationScore !== undefined) {
    entries.push({ label: 'DEDICATION', value: formatDedicationScore(card.dedicationScore), accent: true })
  }
  const m = main.value
  if (m) {
    entries.push({
      label: m.championName ? `${m.championName.toUpperCase()} GAMES` : 'MAIN GAMES',
      value: formatCount(m.games),
      accent: false,
    })
  }
  const winRate = ranked.value?.winRate
  if (winRate !== null && winRate !== undefined) {
    entries.push({ label: 'RANKED WIN RATE', value: formatPercentage(winRate, 1), accent: false })
  }
  return entries.slice(0, 3)
})
</script>

<template>
  <div
    :style="{
      display: 'flex',
      flexDirection: 'column',
      width: '100%',
      height: '100%',
      padding: '56px 64px',
      backgroundColor: INK,
      backgroundImage: BACKDROP,
      color: TEXT,
      fontFamily: 'Inter, sans-serif',
    }"
  >
    <!-- Wordmark row; the platform id doubles as the region label the
         leaderboard shows as a flag. -->
    <div :style="{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }">
      <div :style="{ display: 'flex', alignItems: 'center' }">
        <svg
          width="44"
          height="44"
          viewBox="0 0 64 64"
        >
          <path
            d="M13 47V21l15 17L51 15"
            fill="none"
            :stroke="ROSEGOLD"
            stroke-width="8"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
        <div :style="{ display: 'flex', marginLeft: '12px', fontSize: '34px', fontWeight: 700, letterSpacing: '-0.5px' }">
          <span :style="{ color: ROSEGOLD_LIGHT }">True</span>
          <span :style="{ color: TEXT }">Main</span>
        </div>
      </div>
      <div
        v-if="card?.platformId"
        :style="{ display: 'flex', fontSize: '22px', fontWeight: 600, color: TEXT_MUTED }"
      >
        {{ card.platformId.toUpperCase() }}
      </div>
    </div>

    <div :style="{ display: 'flex', alignItems: 'center', marginTop: '52px' }">
      <div :style="{ display: 'flex', flexDirection: 'column', flexGrow: 1 }">
        <div :style="{ display: 'flex', fontSize: riotId && riotId.length > 18 ? '58px' : '74px', fontWeight: 700, letterSpacing: '-2px', color: TEXT }">
          {{ riotId ?? 'True main profiles' }}
        </div>
        <div :style="{ display: 'flex', alignItems: 'center', marginTop: '10px' }">
          <div :style="{ display: 'flex', fontSize: '34px', fontWeight: 700, color: riotId ? rankColor : TEXT_MUTED }">
            {{ riotId ? rankLine : 'Ranked progress, mains and match history' }}
          </div>
          <div
            v-if="recordLine"
            :style="{ display: 'flex', marginLeft: '18px', fontSize: '26px', fontWeight: 600, color: TEXT_DIM }"
          >
            {{ recordLine }}
          </div>
        </div>
      </div>

      <!-- Main champion: portrait + name + the share of games behind it. -->
      <div
        v-if="main"
        :style="{
          display: 'flex',
          alignItems: 'center',
          marginLeft: '32px',
          padding: '16px 24px 16px 16px',
          borderRadius: '24px',
          backgroundColor: PANEL,
          border: `1px solid ${PANEL_BORDER}`,
        }"
      >
        <div
          :style="{
            display: 'flex',
            width: '104px',
            height: '104px',
            borderRadius: '18px',
            backgroundColor: INK,
            border: `2px solid ${ROSEGOLD}`,
            backgroundImage: main.championIconUrl ? `url(${main.championIconUrl})` : undefined,
            backgroundSize: '104px 104px',
          }"
        />
        <div :style="{ display: 'flex', flexDirection: 'column', marginLeft: '20px' }">
          <div :style="{ display: 'flex', fontSize: '17px', fontWeight: 600, color: TEXT_DIM }">
            {{ main.isOtp ? 'ONE-TRICK' : 'MAIN' }}
          </div>
          <div :style="{ display: 'flex', marginTop: '4px', fontSize: '38px', fontWeight: 700, color: TEXT }">
            {{ main.championName ?? `Champion ${main.championId}` }}
          </div>
          <div :style="{ display: 'flex', marginTop: '4px', fontSize: '22px', color: TEXT_MUTED }">
            {{ formatPercentage(main.playRate, 0) }} of games
          </div>
        </div>
      </div>
    </div>

    <div
      v-if="tiles.length > 0"
      :style="{ display: 'flex', marginTop: 'auto' }"
    >
      <div
        v-for="tile in tiles"
        :key="tile.label"
        :style="{
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'center',
          flexGrow: 1,
          flexBasis: 0,
          marginRight: '20px',
          padding: '22px 28px',
          borderRadius: '22px',
          backgroundColor: PANEL,
          border: `1px solid ${tile.accent ? ROSEGOLD : PANEL_BORDER}`,
        }"
      >
        <div :style="{ display: 'flex', fontSize: '18px', fontWeight: 600, color: TEXT_DIM }">
          {{ tile.label }}
        </div>
        <div :style="{ display: 'flex', marginTop: '6px', fontSize: '52px', fontWeight: 700, color: tile.accent ? ROSEGOLD : TEXT }">
          {{ tile.value }}
        </div>
      </div>
    </div>

    <div
      :style="{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        marginTop: tiles.length > 0 ? '28px' : 'auto',
        paddingTop: '22px',
        borderTop: `1px solid ${GOLD_HAIRLINE}`,
        fontSize: '22px',
        color: TEXT_DIM,
      }"
    >
      <div :style="{ display: 'flex' }">
        {{ riotId ? 'Tracked as a true main on TrueMain' : 'The OTP leaderboard, ranked by dedication' }}
      </div>
      <div :style="{ display: 'flex', fontWeight: 600, color: TEXT_MUTED }">
        truemain.lol
      </div>
    </div>
  </div>
</template>
