<script setup lang="ts">
// Design-system playground (`pages/dev/design-system.vue`): the elevation ladder, the
// materials and the picked / not-picked treatment.

// The brand mark stands in for perk artwork: this page fetches nothing, and the
// treatment needs a *coloured* subject or `grayscale` has nothing to show.
const PERK_STATES = [
  { label: 'selected-perk', class: 'selected-perk' },
  { label: 'deselected', class: 'deselected' },
  { label: 'deselected', class: 'deselected' },
]

const ELEVATION = [
  { token: '--ui-bg', util: 'bg-default', hex: '#0b0b0d', use: 'Page' },
  { token: '--ui-bg-muted', util: 'bg-muted', hex: '#131317', use: 'Recessed — inset strips, empty states' },
  { token: '--ui-bg-elevated', util: 'bg-elevated', hex: '#1b1b20', use: 'Raised — cards, rows' },
  { token: '--ui-bg-accented', util: 'bg-accented', hex: '#24242a', use: 'Interactive — hover, nested' },
]
</script>

<template>
  <div class="flex flex-col gap-10">
    <!-- ─── Elevation ────────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Elevation ladder"
      subtitle="Four opaque steps. Nested here on purpose — if two steps read as one, the nesting below collapses visually and the ladder needs re-spacing."
    >
      <div class="flex flex-col gap-4">
        <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
          <div
            v-for="step in ELEVATION"
            :key="step.token"
            class="flex flex-col gap-2 rounded-lg border border-default p-3"
            :class="step.util"
          >
            <span class="stat-label">{{ step.util }}</span>
            <span class="font-mono text-xs text-muted">{{ step.hex }}</span>
            <span class="text-xs text-dimmed">{{ step.use }}</span>
          </div>
        </div>

        <!-- The real test: the steps stacked, not side by side. -->
        <div class="rounded-lg bg-default p-4">
          <span class="stat-label">bg-default</span>
          <div class="mt-2 rounded-lg bg-muted p-4">
            <span class="stat-label">bg-muted</span>
            <div class="mt-2 rounded-lg bg-elevated p-4">
              <span class="stat-label">bg-elevated</span>
              <div class="mt-2 rounded-lg bg-accented p-4">
                <span class="stat-label">bg-accented</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Materials ────────────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Materials"
      subtitle="`surface` is the app-wide panel, edged with `keycap` (lit top, shaded bottom, both inset); `surface-hover` steps it up the ladder on hover; `surface-blur` blurs what moves behind it, for the few panes over art or the star field. The only translucent material is `glass-bar`, on the floating header — the page itself is the live sample."
    >
      <div class="grid gap-3 sm:grid-cols-2">
        <div class="surface rounded-xl p-4">
          <p class="text-sm font-medium text-default">
            surface
          </p>
          <p class="mt-1 text-xs text-muted">
            Opaque fill, neutral hairline, one soft shadow.
          </p>
        </div>
        <div class="surface surface-hover cursor-pointer rounded-xl p-4">
          <p class="text-sm font-medium text-default">
            surface + surface-hover
          </p>
          <p class="mt-1 text-xs text-muted">
            Hover me — the fill moves up one step.
          </p>
        </div>
      </div>
    </SectionCard>

    <!-- ─── Selection state ──────────────────────────────────────────────── -->
    <SectionCard
      :level="2"
      title="Picked vs. not picked"
      subtitle="`deselected` and `selected-perk`, the pair behind every rune panel. The artwork alone is not a reliable signal — a selected grey rune reads exactly like its unpicked neighbours — so the faint primary ring carries the state and the siblings fade out of the way. Shown on the brand mark because this page fetches nothing; the treatment is the same."
    >
      <div class="flex flex-wrap items-start gap-6">
        <div
          v-for="(state, index) in PERK_STATES"
          :key="`${state.label}-${index}`"
          class="flex flex-col items-center gap-1"
        >
          <img
            src="/brand/truemain-mark.svg"
            alt=""
            width="40"
            height="40"
            class="size-10 rounded-full transition"
            :class="state.class"
          >
          <span class="stat-label">{{ state.label }}</span>
        </div>
      </div>
    </SectionCard>
  </div>
</template>
