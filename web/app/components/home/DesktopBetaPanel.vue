<script setup lang="ts">
// The desktop companion's place on the home page (#1719): right under the
// strongest picks and their mains, since the app is those two answers brought
// into champion select. It says it is a beta before anything else — the
// installers are unsigned and the app is still moving.
</script>

<template>
  <section class="mx-auto max-w-6xl px-4 pb-20 md:px-6">
    <div class="surface flex flex-col gap-6 rounded-2xl p-6 sm:flex-row sm:items-center sm:p-8">
      <div class="flex size-12 shrink-0 items-center justify-center rounded-xl bg-primary/10 ring-1 ring-primary/30">
        <UIcon
          name="i-lucide-monitor-down"
          class="size-6 text-primary"
        />
      </div>
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
        <h2 class="mt-2 text-xl font-semibold tracking-tight text-balance text-highlighted sm:text-2xl">
          The same picks and mains, in your champion select.
        </h2>
        <p class="mt-2 max-w-2xl text-sm text-muted sm:text-base">
          The TrueMain app reads your champion select, ranks the picks from your own pool against the draft, guesses the
          enemy lanes, and hands you the build to run. It is a beta: expect rough edges while it grows. For macOS and Windows.
        </p>
      </div>
      <UButton
        to="/download"
        size="lg"
        icon="i-lucide-download"
        label="Get the beta"
        class="shrink-0 self-start sm:self-center"
      />
    </div>
  </section>
</template>
