<script setup lang="ts">
import type { ChampionOverviewResponse } from '~~/shared/types/champions'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { formatCompactCount } from '~~/shared/utils/counts'

// Variant H home, first screen: the arena. A short headline and the search
// up top, then the patch's most present champions on a ring that stands in
// the eclipse — the WebGL backdrop is sized so its dark orb sits right behind
// the front card and its rim crowns it. The two lifetime totals flank the
// ring's controls. Every figure comes from the page's payloads.
const props = defineProps<{
  overview: ChampionOverviewResponse | null | undefined
  overviewPending: boolean
  tierlistPending: boolean
  championsById: Map<number, ChampionStaticListItem>
  gamesAnalyzed: number
  trackedTruemains: number | null
}>()

const fx = useFx()
// The WebGL eclipse renders on the server like everywhere else; a lite machine
// swaps it for the static CSS eclipse once mounted (a client-only decision, so
// the first client render matches the server's).
const lite = ref(false)
onMounted(() => {
  lite.value = fx.value.level === 'lite'
})

const patch = computed(() => props.overview?.patchVersion ?? null)
const rows = computed(() => props.overview?.topRows ?? [])
const showGames = computed(() => !props.overviewPending && props.gamesAnalyzed > 0)
</script>

<template>
  <section
    class="h-arena"
    aria-labelledby="h-arena-title"
  >
    <div class="h-arena__head">
      <p class="h-eyebrow">
        <span
          class="h-eyebrow__pulse"
          aria-hidden="true"
        />
        <template v-if="patch">
          <span class="text-highlighted">Patch {{ patch }}</span>
          <span
            class="h-eyebrow__sep"
            aria-hidden="true"
          />
        </template>
        <span>Most played and banned</span>
      </p>

      <h1
        id="h-arena-title"
        class="h-title"
      >
        <span
          class="h-lit h-lit--load"
          data-h-spot
        >Real builds from</span>
        {{ ' ' }}
        <span
          class="h-lit h-lit--rose h-lit--load"
          data-h-spot
        >real mains</span><span
          class="h-lit h-lit--load"
          data-h-spot
        >.</span>
      </h1>

      <div class="h-arena__search">
        <LazyAppSearch
          variant="field"
          size="lg"
          class="w-full"
          hydrate-on-idle
        />
      </div>
    </div>

    <DesignHCarousel
      :rows="rows"
      :champions-by-id="championsById"
      :pending="tierlistPending"
    >
      <template #backdrop>
        <AppBackdrop v-if="!lite" />
        <div
          v-else
          class="h-eclipse"
        />
      </template>

      <template #start>
        <dl class="h-tally">
          <dt>Main games analyzed</dt>
          <dd>
            <USkeleton
              v-if="overviewPending"
              class="h-6 w-16"
            />
            <FxCount
              v-else-if="showGames"
              :value="gamesAnalyzed"
              :format="formatCompactCount"
            />
            <span v-else>—</span>
          </dd>
        </dl>
      </template>

      <template #end>
        <dl
          v-if="trackedTruemains !== null"
          class="h-tally h-tally--end"
        >
          <dt>Truemains tracked</dt>
          <dd>
            <FxCount
              :value="trackedTruemains"
              :format="formatCompactCount"
            />
          </dd>
        </dl>
      </template>
    </DesignHCarousel>
  </section>
</template>
