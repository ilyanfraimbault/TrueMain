<script setup lang="ts">
import type { DesignHomeData } from '~/composables/useDesign'
import { formatCompactCount } from '~~/shared/utils/counts'

// Variant E home, round 3 (owner's picks): search first with the visitor's own
// champions, then the meta lane by lane — each lane also the way into the tier
// list — then the truemains. No splash art, no cursor light.
const props = defineProps<DesignHomeData>()

const patch = computed(() => props.overview?.patchVersion ?? null)
const fullCount = (value: number) => Math.round(value).toLocaleString('en-US')
const compactCount = (value: number) => formatCompactCount(Math.round(value))
</script>

<template>
  <div class="e-hx">
    <section class="e-hx-hero">
      <p class="e-hx-eyebrow">
        <span class="e-hx-dot" />
        <span v-if="patch">Patch {{ patch }} · live from ranked</span>
        <span v-else>Champion intelligence</span>
      </p>
      <h1 class="e-hx-title">
        Real builds from <span class="text-primary">real mains</span>.
      </h1>
      <p class="e-hx-lede">
        Builds, runes and skill orders from the players who truly mastered your champion.
      </p>

      <LazyAppSearch
        variant="field"
        size="lg"
        class="e-hx-search"
        hydrate-on-idle
      />

      <dl class="e-hx-stats">
        <div>
          <dt>Main games analyzed</dt>
          <dd>
            <USkeleton
              v-if="overviewPending"
              class="h-6 w-28"
            />
            <FxCount
              v-else-if="gamesAnalyzed > 0"
              :value="gamesAnalyzed"
              :format="fullCount"
            />
          </dd>
        </div>
        <div v-if="trackedTruemains !== null">
          <dt>Truemains tracked</dt>
          <dd>
            <FxCount
              :value="trackedTruemains"
              :format="compactCount"
            />
          </dd>
        </div>
      </dl>

      <DesignEFavorites :champions-by-id="championsById" />
    </section>

    <DesignEMetaBoard :champions-by-id="championsById" />

    <DesignETruemainsBoard
      :rows="truemainRows"
      :champions-by-id="championsById"
      :loading="truemainsInitialLoading"
      :patch="ddragonPatch"
      :tracked="trackedTruemains"
    />
  </div>
</template>
