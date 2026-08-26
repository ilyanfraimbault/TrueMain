<script setup lang="ts">
import type { ChampionIndexLink } from '~~/shared/types/champion-index'

/**
 * Every champion, A→Z, as real `<a href="/champions/{slug}">Name</a>` (#1209).
 *
 * The one block on `/champions` — and on every champion page — that puts the
 * site's ~174 champion URLs into the server-rendered HTML with inbound links.
 * Before it, the only `/champions/*` anchor anywhere on the site was
 * `/champions/tierlist`: the directory's rows are a `role="button"` by design
 * (#147) and the whole grid is client-only (#149), so a crawler saw a page
 * titled "Champion Builds" containing no champion and no link to one.
 *
 * Visible, never `sr-only` — hiding indexable text from the reader is cloaking,
 * and this is also the fastest way for a human to jump to a champion whose name
 * they already know, which is most visits.
 *
 * Renders nothing when the names failed to load: an anchor labelled
 * `Champion 103` is worse than no anchor, so `championIndexLinks` drops
 * nameless champions and this drops the empty block.
 *
 * The `href` is built with `pathFor` like every other champion link in the app
 * — the slug map is app-wide state loaded before the first render (#1124), so
 * it resolves synchronously during SSR. No `position` query: these must be the
 * exact canonical URLs the sitemap advertises, not filtered variants of them.
 */
const props = defineProps<{
  champions: ChampionIndexLink[]
  /** Section heading. */
  title?: string
  /** One line under the heading, for pages where the block needs framing. */
  subtitle?: string
}>()

const { pathFor } = useChampionSlugs()

const hasChampions = computed(() => props.champions.length > 0)
</script>

<template>
  <SectionCard
    v-if="hasChampions"
    :title="title ?? 'All champions'"
    :subtitle="subtitle"
    :level="2"
  >
    <!-- A plain wrapped list of text links rather than the icon grid above it:
         the grid already carries the pictures, and what is missing from the
         HTML is the *words*. `flex-wrap` over a column layout so 174 names read
         as one index instead of a scroll. -->
    <!-- The link styling lives here, as `[&_a]:*` variants, not on each of the
         174 anchors: repeated per-anchor it added ~25 kB of identical class
         attributes to the HTML of the site's busiest page, which is a poor
         trade for a block whose whole job is to be cheap enough to put on
         every page. -->
    <ul class="flex flex-wrap gap-x-3 gap-y-1.5 [&_a]:text-sm [&_a]:text-muted [&_a]:underline-offset-4 [&_a]:hover:text-default [&_a]:hover:underline [&_a]:focus-visible:outline-none [&_a]:focus-visible:ring-2 [&_a]:focus-visible:ring-primary">
      <li
        v-for="champion in champions"
        :key="champion.championId"
      >
        <NuxtLink :to="pathFor(champion.championId)">
          {{ champion.name }}
        </NuxtLink>
      </li>
    </ul>
  </SectionCard>
</template>
