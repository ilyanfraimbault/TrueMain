<script setup lang="ts">
// The desktop companion's place on the home page (#1719): a banner right under
// the hero, above the strongest picks and their mains — the app is those two
// answers brought into champion select, so it is announced before them. It says
// it is a beta up front: the installers are unsigned and the app is still moving.
const FEATURES = [
  { icon: 'i-lucide-list-ordered', label: 'Picks from your pool' },
  { icon: 'i-lucide-crosshair', label: 'Enemy lanes read for you' },
  { icon: 'i-lucide-hammer', label: 'The build against the draft' },
] as const
</script>

<template>
  <section class="mx-auto max-w-6xl px-4 pb-16 md:px-6">
    <div class="surface relative isolate overflow-hidden rounded-2xl p-6 sm:p-8">
      <!-- The banner's light: a rose-gold wash from behind the icon, and a
           hairline of the same colour along the top edge. -->
      <div
        aria-hidden="true"
        class="pointer-events-none absolute -left-16 -top-24 -z-10 size-72 rounded-full bg-primary/8 blur-3xl"
      />
      <div
        aria-hidden="true"
        class="pointer-events-none absolute inset-x-8 top-0 h-px bg-gradient-to-r from-transparent via-primary/60 to-transparent"
      />

      <div class="flex flex-col gap-6 lg:flex-row lg:items-center lg:gap-8">
        <DesktopAppIcon class="size-16 sm:size-20" />

        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-center gap-2">
            <p class="eyebrow">
              Desktop app
            </p>
            <UBadge
              color="primary"
              variant="subtle"
              size="sm"
              label="Beta"
            />
          </div>
          <h2 class="mt-2 text-2xl font-semibold tracking-tight text-balance text-highlighted sm:text-3xl">
            TrueMain, in your champion select.
          </h2>
          <ul class="mt-4 flex flex-wrap gap-2">
            <li
              v-for="feature in FEATURES"
              :key="feature.label"
              class="inline-flex items-center gap-1.5 rounded-full bg-default/60 px-3 py-1 text-sm text-default ring-1 ring-default"
            >
              <UIcon
                :name="feature.icon"
                class="size-4 text-primary"
              />
              {{ feature.label }}
            </li>
          </ul>
        </div>

        <div class="flex shrink-0 flex-col items-start gap-2.5 lg:items-center">
          <UButton
            to="/download"
            size="xl"
            icon="i-lucide-download"
            label="Get the beta"
          />
          <p class="flex items-center gap-2 text-xs text-dimmed">
            <UIcon
              name="i-simple-icons-apple"
              class="size-3.5"
            />
            <UIcon
              name="i-simple-icons-windows"
              class="size-3"
            />
            For macOS and Windows
          </p>
        </div>
      </div>
    </div>
  </section>
</template>
