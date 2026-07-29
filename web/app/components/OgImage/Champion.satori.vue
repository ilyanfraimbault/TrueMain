<script setup lang="ts">
import type { ChampionOgCard } from '~~/shared/types/og-card'
import { formatPercentage } from '~~/shared/utils/ddragon'
import {
  BACKDROP,
  formatCount,
  formatEloBracket,
  formatPosition,
  GOLD_HAIRLINE,
  INK,
  PANEL,
  PANEL_BORDER,
  ROSEGOLD,
  ROSEGOLD_LIGHT,
  TEXT,
  TEXT_DIM,
  TEXT_MUTED,
  TIER_COLOR,
} from './theme'

/**
 * Share card for `/champions/{id}` (#926).
 *
 * The props are *identifiers only* — everything the page knew at SSR time, which
 * is the route param plus whatever filters the shared URL pinned. The measured
 * numbers are resolved here, at render time, from `/api/og/champion/{id}`; see
 * that handler for why the page itself cannot supply them.
 *
 * Fallback contract, in one place:
 *   - no stats  → the numbers block is not rendered at all (no 0%, no dashes
 *     standing in for a measurement, no "—" that could read as a real tier);
 *   - no name   → the card drops to the plain branded state;
 *   - no ban rate (a patch predating #920's ingestion) → that one tile is
 *     dropped rather than zeroed, exactly as the directory renders an em dash;
 *   - portraits are painted as `background-image`, so a DDragon failure leaves
 *     an empty rose-gold plate instead of breaking the render.
 *
 * Everything is styled with inline objects — see `theme.ts` for why.
 */
const props = defineProps<{
  /** Route param; arrives as a string once it has been through the OG URL encoding. */
  championId?: number | string
  /** Pinned lane from `?position=`, when the shared URL had one. */
  position?: string
  /** Pinned rank filter from `?elo=`, when the shared URL had one. */
  eloBracket?: string
  /** Pinned patch from `?patch=`, when the shared URL had one. */
  patch?: string
}>()

// A render must never 500: a crawler that gets an error page shows *no* preview,
// which is strictly worse than the branded card. So every failure — bad id,
// upstream down, malformed payload — collapses into `card === null`.
const card = await (async (): Promise<ChampionOgCard | null> => {
  const id = Number(props.championId)
  if (!Number.isInteger(id) || id <= 0) return null
  return $fetch<ChampionOgCard>(`/api/og/champion/${id}`, {
    query: {
      position: props.position || undefined,
      eloBracket: props.eloBracket || undefined,
      patch: props.patch || undefined,
    },
  }).catch(() => null)
})()

const stats = computed(() => card?.stats ?? null)
const championName = computed(() => card?.championName ?? null)
const tierColor = computed(() => (stats.value ? TIER_COLOR[stats.value.tier.toUpperCase()] ?? null : null))

/** The lane/rank caption. Only ever built from values the API actually returned. */
const subtitle = computed(() => {
  if (!stats.value) return null
  return `${formatPosition(stats.value.position)} · ${formatEloBracket(stats.value.eloBracket)}`
})

/**
 * The tiles, assembled rather than templated, so "this stat is unknown" is a
 * missing entry instead of a rendered placeholder.
 */
const tiles = computed(() => {
  const s = stats.value
  if (!s) return []
  const entries = [
    { label: 'WIN RATE', value: formatPercentage(s.winRate, 1), accent: true },
    { label: 'PICK RATE', value: formatPercentage(s.pickRate, 1), accent: false },
  ]
  if (s.banRate !== null) {
    entries.push({ label: 'BAN RATE', value: formatPercentage(s.banRate, 1), accent: false })
  }
  return entries
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
    <!-- Wordmark row. The M-check mark is drawn as a flat stroke rather than
         the gradient the header uses: Satori rasterises SVG gradients
         inconsistently, and a solid rose-gold stroke reads identically at
         this size. -->
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
        v-if="stats"
        :style="{ display: 'flex', fontSize: '22px', fontWeight: 600, color: TEXT_MUTED }"
      >
        PATCH {{ stats.patch }}
      </div>
    </div>

    <!-- Identity block: portrait + name + resolved slice. -->
    <div :style="{ display: 'flex', alignItems: 'center', marginTop: '52px' }">
      <div
        :style="{
          display: 'flex',
          width: '176px',
          height: '176px',
          borderRadius: '28px',
          backgroundColor: PANEL,
          border: `2px solid ${ROSEGOLD}`,
          backgroundImage: card?.championIconUrl ? `url(${card.championIconUrl})` : undefined,
          backgroundSize: '176px 176px',
        }"
      />
      <div :style="{ display: 'flex', flexDirection: 'column', marginLeft: '36px', flexGrow: 1 }">
        <div :style="{ display: 'flex', fontSize: championName && championName.length > 14 ? '68px' : '84px', fontWeight: 700, letterSpacing: '-2px', color: TEXT }">
          {{ championName ?? 'Champion builds' }}
        </div>
        <div
          v-if="subtitle"
          :style="{ display: 'flex', marginTop: '8px', fontSize: '30px', fontWeight: 600, color: TEXT_MUTED }"
        >
          {{ subtitle }}
        </div>
        <div
          v-else
          :style="{ display: 'flex', marginTop: '8px', fontSize: '30px', fontWeight: 600, color: TEXT_MUTED }"
        >
          Runes, items and skill orders from true mains
        </div>
      </div>
      <!-- The tier letter is the page's headline verdict; it only appears when
           the directory actually assigned one. -->
      <div
        v-if="stats && tierColor"
        :style="{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          width: '132px',
          height: '132px',
          borderRadius: '28px',
          backgroundColor: PANEL,
          border: `2px solid ${tierColor}`,
        }"
      >
        <div :style="{ display: 'flex', fontSize: '76px', fontWeight: 700, lineHeight: 1, color: tierColor }">
          {{ stats.tier.toUpperCase() }}
        </div>
        <div :style="{ display: 'flex', marginTop: '6px', fontSize: '17px', fontWeight: 600, color: TEXT_DIM }">
          TIER
        </div>
      </div>
    </div>

    <!-- Measured numbers. Absent entirely when we hold no aggregate. -->
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

    <!-- Footer: the sample behind the numbers, and the brand. `marginTop: auto`
         lives on whichever block comes last so the branded card (no tiles)
         still pins its footer to the bottom. -->
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
        {{ stats ? `${formatCount(stats.games)} ranked games from true mains` : 'Stats computed from true main players only' }}
      </div>
      <div :style="{ display: 'flex', fontWeight: 600, color: TEXT_MUTED }">
        truemain.lol
      </div>
    </div>
  </div>
</template>
