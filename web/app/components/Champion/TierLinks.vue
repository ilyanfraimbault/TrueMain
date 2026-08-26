<script setup lang="ts">
import type { ChampionIndexTierGroup } from '~~/shared/types/champion-index'
import { POSITION_BY_VALUE } from '~/utils/positions'

/**
 * The tier list in words: each tier's champions as named links, with the lane
 * the tier was computed for (#1209).
 *
 * `/champions/tierlist` shipped 55 kB of HTML containing zero rows — the table
 * is client-only (#149) and its chip is deliberately a portrait with the name
 * in a tooltip (see decisions.md), so even server-rendered it would carry no
 * champion *name*. This block is what the page can actually rank for on
 * "champion tier list" queries, and it is the same content the chips show,
 * written out.
 *
 * A champion appears once, under its strongest tier — see `championIndexTiers`
 * for why. Visible, never `sr-only`: a reader scanning for a name is better
 * served by a list of names than by hovering 300 portraits.
 *
 * Renders nothing when the ranking failed to load or the filters match nothing,
 * rather than an empty card saying so — the page's own no-data state already
 * says it once.
 */
const props = defineProps<{
  tiers: ChampionIndexTierGroup[]
  /** Patch the ranking was computed for; printed in the subtitle when known. */
  patch?: string | null
  title?: string
}>()

const { pathFor } = useChampionSlugs()

const hasTiers = computed(() => props.tiers.length > 0)

const subtitle = computed(() =>
  props.patch
    ? `Ranked on patch ${props.patch}. A champion is listed once, on its strongest lane.`
    : 'A champion is listed once, on its strongest lane.',
)

function laneLabel(position: string): string | null {
  return POSITION_BY_VALUE.get(position)?.label ?? null
}
</script>

<template>
  <SectionCard
    v-if="hasTiers"
    :title="title ?? 'Champions by tier'"
    :subtitle="subtitle"
    :level="2"
  >
    <div class="space-y-3">
      <!-- One row per tier: the letter, then the names. `dl`/`dt`/`dd` rather
           than nested lists — each tier letter *labels* the champions after it,
           which is what a description list is, and it keeps the tier letter out
           of the anchor text. -->
      <!-- Link styling as `[&_a]:*` variants rather than per-anchor classes —
           same reason as the A→Z index: ~300 copies of an identical class
           attribute is most of what this block would weigh. -->
      <dl class="space-y-3 [&_a]:text-sm [&_a]:text-muted [&_a]:underline-offset-4 [&_a]:hover:text-default [&_a]:hover:underline [&_a]:focus-visible:outline-none [&_a]:focus-visible:ring-2 [&_a]:focus-visible:ring-primary">
        <div
          v-for="group in tiers"
          :key="group.tier"
          class="flex flex-wrap items-baseline gap-x-3 gap-y-1.5"
        >
          <dt class="shrink-0">
            <TierBadge :tier="group.tier" />
          </dt>
          <dd
            v-for="entry in group.entries"
            :key="`${group.tier}-${entry.championId}`"
            class="text-sm"
          >
            <NuxtLink :to="pathFor(entry.championId)">
              {{ entry.name }}
            </NuxtLink>
            <!-- The lane is context, not part of the link: keeping it outside
                 the anchor keeps the anchor text the champion's name alone. -->
            <span
              v-if="laneLabel(entry.position)"
              class="text-xs text-dimmed"
            >
              {{ ' ' }}{{ laneLabel(entry.position) }}
            </span>
          </dd>
        </div>
      </dl>
    </div>
  </SectionCard>
</template>
