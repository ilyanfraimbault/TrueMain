<script setup lang="ts">
import type { DesignHomeData } from '~/composables/useDesign'
import { formatCompactCount } from '~~/shared/utils/counts'

// Variant A — Liquid Glass. The eclipse stays the hero's world; everything that
// sits on it is glass that bends it: the search capsule, the stat pills, the
// lens that follows the cursor, the plates on the champion cards, the closing
// card over a ring of light.
const props = defineProps<DesignHomeData>()

const fx = useFx()
const hero = ref<HTMLElement | null>(null)
const mounted = ref(false)
onMounted(() => (mounted.value = true))
const lensOn = computed(() => mounted.value && fx.value.refraction && fx.value.finePointer && !fx.value.reducedMotion)

const fullCount = (value: number) => Math.round(value).toLocaleString('en-US')
const compactCount = (value: number) => formatCompactCount(Math.round(value))

const patch = computed(() => props.overview?.patchVersion ?? null)
</script>

<template>
  <div class="dsa-home">
    <!-- ─── Hero ─────────────────────────────────────────────────────────── -->
    <section
      ref="hero"
      class="dsa-hero relative -mt-[68px] flex min-h-[min(100svh,920px)] flex-col items-center justify-center overflow-hidden"
    >
      <AppBackdrop />
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-x-0 bottom-0 -z-10 h-48 bg-gradient-to-b from-transparent to-default"
      />
      <DesignALens
        v-if="lensOn"
        :host="hero"
      />

      <div class="relative z-10 mx-auto flex max-w-4xl flex-col items-center px-6 pb-24 pt-40 text-center sm:pt-48">
        <p
          class="dsa-in dsa-eyebrow-pill"
          style="--d: 0ms"
        >
          <span class="dsa-live-dot" />
          <span v-if="patch">Patch {{ patch }} · live from ranked</span>
          <span v-else>Champion intelligence</span>
        </p>

        <h1 class="dsa-title mt-7">
          <span class="dsa-word" style="--d: 120ms">Real</span>{{ ' ' }}
          <span class="dsa-word" style="--d: 200ms">builds</span>{{ ' ' }}
          <span class="dsa-word" style="--d: 280ms">from</span>
          <br>
          <span class="dsa-word dsa-shine" style="--d: 380ms">real mains.</span>
        </h1>

        <p
          class="dsa-in mt-6 max-w-xl text-base leading-relaxed text-muted sm:text-lg"
          style="--d: 560ms"
        >
          Builds, runes and skill orders from the players who truly mastered your champion.
        </p>

        <FxLiquidGlass
          :radius="999"
          :bezel="20"
          :depth="22"
          :blur="0.6"
          class="dsa-in dsa-search-glass mt-10 w-full max-w-xl p-1.5"
          style="--d: 680ms"
        >
          <LazyAppSearch
            variant="field"
            size="lg"
            class="w-full"
            hydrate-on-idle
          />
        </FxLiquidGlass>

        <dl
          class="dsa-in mt-8 flex flex-wrap items-center justify-center gap-3 text-sm"
          style="--d: 820ms"
        >
          <FxLiquidGlass
            :radius="999"
            :bezel="12"
            :depth="10"
            class="flex items-center gap-2 py-2 pl-3 pr-4"
          >
            <UIcon
              name="i-lucide-database"
              class="size-4 text-primary"
            />
            <USkeleton
              v-if="overviewPending"
              class="h-4 w-36"
            />
            <template v-else-if="gamesAnalyzed > 0">
              <dt class="sr-only">
                Main games analyzed
              </dt>
              <dd class="text-muted">
                <span class="font-semibold text-highlighted"><FxCount
                  :value="gamesAnalyzed"
                  :format="fullCount"
                /></span> main games analyzed
              </dd>
            </template>
          </FxLiquidGlass>
          <FxLiquidGlass
            v-if="trackedTruemains !== null"
            :radius="999"
            :bezel="12"
            :depth="10"
            class="flex items-center gap-2 py-2 pl-3 pr-4"
          >
            <UIcon
              name="i-lucide-users"
              class="size-4 text-primary"
            />
            <dt class="sr-only">
              Truemains tracked
            </dt>
            <dd class="text-muted">
              <span class="font-semibold text-highlighted"><FxCount
                :value="trackedTruemains"
                :format="compactCount"
              /></span> truemains tracked
            </dd>
          </FxLiquidGlass>
        </dl>
      </div>

      <div
        aria-hidden="true"
        class="dsa-in dsa-scroll-cue absolute inset-x-0 bottom-8 mx-auto"
        style="--d: 1200ms"
      >
        <span />
      </div>
    </section>

    <div class="relative">
      <AppBackdrop variant="stars" />
      <div
        aria-hidden="true"
        class="dsa-ambient pointer-events-none absolute inset-0 -z-10"
      />

      <!-- ─── This patch: the champion rail ─────────────────────────────── -->
      <section class="pb-16 pt-10">
        <div class="mx-auto mb-4 max-w-6xl px-6">
          <p
            v-reveal
            class="eyebrow"
          >
            This patch{{ patch ? ` · ${patch}` : '' }}
          </p>
          <h2
            v-reveal="{ delay: 80, effect: 'blur' }"
            class="dsa-h2 mt-3 max-w-2xl"
          >
            The champions everyone is playing — and banning.
          </h2>
        </div>
        <DesignAChampionRail
          :rows="overview?.topRows ?? []"
          :champions-by-id="championsById"
          :pending="tierlistPending"
        />
      </section>

      <!-- ─── Live panels ──────────────────────────────────────────────────── -->
      <section class="mx-auto max-w-6xl px-4 pb-24 md:px-6">
        <div class="mb-10 flex flex-col gap-3">
          <p
            v-reveal
            class="eyebrow"
          >
            Ranked, right now
          </p>
          <h2
            v-reveal="{ delay: 80, effect: 'blur' }"
            class="dsa-h2 max-w-2xl"
          >
            The tier list, and the players who main them.
          </h2>
        </div>
        <div
          v-reveal.stagger="{ step: 120, effect: 'blur' }"
          class="grid gap-6 lg:grid-cols-2"
        >
          <HomeTierlistPanelSkeleton v-if="tierlistPending" />
          <HomeTierlistPanel
            v-else
            :top-rows="overview?.topRows ?? []"
            :champions-by-id="championsById"
          />
          <HomeTruemainsPanel
            :rows="truemainRows"
            :champions-by-id="championsById"
            :initial-loading="truemainsInitialLoading"
            :patch="ddragonPatch"
          />
        </div>
      </section>

      <div v-reveal="{ effect: 'blur' }">
        <HomeDesktopBetaPanel />
      </div>

      <!-- ─── Closing: glass over a ring of light ───────────────────────────── -->
      <section class="dsa-closing relative px-6 py-36 sm:py-48">
        <div
          aria-hidden="true"
          class="dsa-halo"
        />
        <FxLiquidGlass
          v-reveal="{ effect: 'scale' }"
          :radius="44"
          :bezel="40"
          :depth="46"
          :blur="1.5"
          :aberration="0.7"
          class="relative mx-auto max-w-2xl px-8 py-14 text-center sm:px-14"
        >
          <p class="eyebrow">
            Your champion
          </p>
          <h2 class="dsa-h2 mt-3">
            Find <span class="dsa-shine">your</span> real build.
          </h2>
          <p class="mx-auto mt-4 max-w-md text-base text-muted">
            Open the champion you actually play and see what their mains are buying this patch.
          </p>
          <div class="mt-9 flex flex-wrap justify-center gap-3">
            <NuxtLink
              v-magnetic="0.25"
              to="/champions"
              class="dsa-btn dsa-btn--primary"
            >
              <UIcon
                name="i-lucide-swords"
                class="size-4"
              />
              Explore champions
            </NuxtLink>
            <NuxtLink
              v-magnetic="0.25"
              to="/truemains"
              class="dsa-btn dsa-btn--glass"
            >
              <UIcon
                name="i-lucide-trophy"
                class="size-4"
              />
              Truemains leaderboard
            </NuxtLink>
          </div>
        </FxLiquidGlass>
      </section>
    </div>
  </div>
</template>
