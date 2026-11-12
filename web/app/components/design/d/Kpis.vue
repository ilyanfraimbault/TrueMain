<script setup lang="ts">
// Variant D ("Cockpit"): the home's KPI strip — one lit pane split by hairlines,
// three readings, every one from a payload. The numbers count up the first time
// the strip is on screen (`FxCount`), from a skeleton rather than from a
// server-rendered value, so nothing flashes its final figure and then drops to 0.
const props = defineProps<{
  gamesAnalyzed: number
  gamesPending: boolean
  trackedTruemains: number | null
  truemainsLoading: boolean
  patch: string | null
}>()

const mounted = ref(false)
onMounted(() => {
  mounted.value = true
})

const gamesReady = computed(() => mounted.value && !props.gamesPending)
const truemainsReady = computed(() => mounted.value && !props.truemainsLoading)
</script>

<template>
  <section
    class="d-kpis d-panel"
    aria-label="At a glance"
  >
    <span
      class="d-ring"
      data-d-spot
      aria-hidden="true"
    />
    <dl class="d-kpis__grid">
      <div class="d-kpi">
        <dt class="d-kpi__label">
          <UIcon
            name="i-lucide-database"
            class="d-kpi__icon"
          />
          Main games analyzed
        </dt>
        <dd class="d-kpi__value">
          <USkeleton
            v-if="!gamesReady"
            class="h-8 w-40"
          />
          <FxCount
            v-else-if="gamesAnalyzed > 0"
            :value="gamesAnalyzed"
            :duration="1600"
          />
          <span
            v-else
            class="text-dimmed"
          >—</span>
        </dd>
        <dd class="d-kpi__note">
          Lifetime, every patch
        </dd>
      </div>

      <div class="d-kpi">
        <dt class="d-kpi__label">
          <UIcon
            name="i-lucide-users"
            class="d-kpi__icon"
          />
          Truemains tracked
        </dt>
        <dd class="d-kpi__value">
          <USkeleton
            v-if="!truemainsReady"
            class="h-8 w-24"
          />
          <FxCount
            v-else-if="trackedTruemains !== null"
            :value="trackedTruemains"
            :duration="1600"
          />
          <span
            v-else
            class="text-dimmed"
          >—</span>
        </dd>
        <dd class="d-kpi__note">
          On the leaderboard
        </dd>
      </div>

      <div class="d-kpi">
        <dt class="d-kpi__label">
          <UIcon
            name="i-lucide-git-commit-horizontal"
            class="d-kpi__icon"
          />
          Patch
        </dt>
        <dd class="d-kpi__value">
          <USkeleton
            v-if="!gamesReady"
            class="h-8 w-20"
          />
          <span v-else-if="patch">{{ patch }}</span>
          <span
            v-else
            class="text-dimmed"
          >—</span>
        </dd>
        <dd class="d-kpi__note">
          The builds and tiers below
        </dd>
      </div>
    </dl>
  </section>
</template>
