<script setup lang="ts">
import type { BuildSummaryEntityToken, BuildSummaryTone } from '~~/shared/types/champion-build-summary'
import type {
  StaticChampionSpellData,
  StaticItemData,
  StaticPerkData,
  StaticSummonerSpellData,
} from '~~/shared/types/static-data'

/**
 * One named entity inside the build paragraph: its icon, its tone, and the same
 * hover card the icon grid above shows for it (#1143 for the mark, #1147 for
 * the card).
 *
 * The card is resolved **here, client-side**, from the static maps the champion
 * page has already fetched — not carried in the summary payload. That payload
 * exists precisely because naming an item server-side must not drag the ~373 KiB
 * item map into the HTML, and a tooltip needs the whole record: stats, passive,
 * gold. So the paragraph server-renders its words and grows its hover cards at
 * hydration, which costs the page nothing it wasn't already paying.
 *
 * `token.source` and not `token.tone` decides which map to look in: a Domination
 * rune and the Domination tree share a tone and would resolve against the wrong
 * lookup.
 *
 * The tooltip is always rendered and merely *disabled* until its map lands,
 * never `v-if`-ed around the trigger. Reka snapshots the trigger element in
 * `onMounted` and binds the hoverable-content grace-area `pointerleave` to that
 * snapshot, so replacing the node when the item map arrives would leave every
 * mark able to open its card and unable to close it — the #1145 bug, one
 * paragraph later.
 */
const props = defineProps<{
  token: BuildSummaryEntityToken
  /** Client-only on this page (~373 KiB) — absent until it lands. */
  itemsMap?: Record<number, StaticItemData> | null
  perks?: Record<number, StaticPerkData> | null
  summonersMap?: Record<number, StaticSummonerSpellData> | null
  championSpells?: Record<string, StaticChampionSpellData> | null
}>()

const numericId = computed(() => (typeof props.token.id === 'number' ? props.token.id : null))

const item = computed(() =>
  props.token.source === 'item' && numericId.value !== null
    ? props.itemsMap?.[numericId.value] ?? null
    : null,
)
const perk = computed(() =>
  props.token.source === 'perk' && numericId.value !== null
    ? props.perks?.[numericId.value] ?? null
    : null,
)
const summoner = computed(() =>
  props.token.source === 'summoner' && numericId.value !== null
    ? props.summonersMap?.[numericId.value] ?? null
    : null,
)
const ability = computed(() =>
  props.token.source === 'ability'
    ? props.championSpells?.[String(props.token.id)] ?? null
    : null,
)

// Rune *trees* and the pinned opponent are marks without a card: the tree
// tooltip elsewhere on the page is only the tree's own name, which this
// sentence has already written out, and there is no champion body component.
const hasCard = computed(() =>
  Boolean(item.value || perk.value || summoner.value || ability.value),
)

/**
 * Tone → utility. Written out rather than built as `text-rune-${tone}` so
 * Tailwind can see every class it has to generate, and so the mapping from a
 * *semantic* tone to the design system's vocabulary lives in the view instead
 * of in the shared model — see `main.css` for why the five rune tones exist.
 *
 * Summoner spells, abilities and the pinned opponent share `text-highlighted`
 * on purpose: they have no colour of their own in Riot's vocabulary, and the
 * icon beside them already says which kind of thing they are. Inventing three
 * more hues to fill the table would be exactly the rainbow the palette avoids.
 */
const TONE_CLASS: Readonly<Record<BuildSummaryTone, string>> = {
  item: 'text-stat-gold',
  spell: 'text-highlighted',
  ability: 'text-highlighted',
  champion: 'text-highlighted',
  precision: 'text-rune-precision',
  domination: 'text-rune-domination',
  sorcery: 'text-rune-sorcery',
  inspiration: 'text-rune-inspiration',
  resolve: 'text-rune-resolve',
  rune: 'text-highlighted',
}

/** Runes and portraits are round artwork; items, spells and abilities are square. */
const ROUND_TONES: ReadonlySet<BuildSummaryTone> = new Set<BuildSummaryTone>([
  'precision',
  'domination',
  'sorcery',
  'inspiration',
  'resolve',
  'rune',
  'champion',
])

// Plain <img> through the canonical IPX URL rather than `SkeletonImage`: these
// icons sit *inside* sentences, where a pulsing placeholder box mid-line reads
// as a broken word, and there are a dozen of them per paragraph. Same 64×64
// WebP fetch as every other icon on the site, so they share its cache entries.
const canonicalIcon = useCanonicalIcon()
</script>

<template>
  <!-- Disabled, never `v-if`-ed away: the trigger node must not be replaced
       when the maps land (see the note above). -->
  <UTooltip
    :disabled="!hasCard"
    :delay-duration="150"
    :ui="{ content: 'p-0 h-auto max-w-none bg-transparent ring-0 shadow-none text-default' }"
  >
    <!-- The name and its icon never separate across a line break: an orphaned
         icon at the end of a line reads as a bullet. -->
    <span
      class="whitespace-nowrap font-medium"
      :class="[TONE_CLASS[token.tone], hasCard ? 'cursor-help' : '']"
    ><img
      v-if="token.iconUrl"
      :src="canonicalIcon(token.iconUrl)"
      alt=""
      aria-hidden="true"
      :width="16"
      :height="16"
      loading="lazy"
      class="mr-1 inline-block size-4 align-[-0.2em]"
      :class="ROUND_TONES.has(token.tone) ? 'rounded-full' : 'rounded-xs'"
    >{{ token.text }}</span>
    <template #content>
      <GameTooltipSurface>
        <GameTooltipItemBody
          v-if="item"
          :item="item"
        />
        <GameTooltipPerkBody
          v-else-if="perk"
          :perk="perk"
        />
        <GameTooltipSummonerSpellBody
          v-else-if="summoner"
          :spell="summoner"
        />
        <GameTooltipChampionSpellBody
          v-else-if="ability"
          :spell="ability"
        />
      </GameTooltipSurface>
    </template>
  </UTooltip>
</template>
