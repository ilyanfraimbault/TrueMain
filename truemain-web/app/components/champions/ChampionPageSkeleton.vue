<template>
  <section class="space-y-8">
    <header class="flex flex-col gap-6 md:flex-row md:items-start md:justify-between">
      <div class="flex items-center gap-5">
        <USkeleton class="size-28 shrink-0 rounded-3xl" />

        <div class="space-y-4">
          <USkeleton class="h-10 w-48 rounded-lg" />
          <div class="flex gap-10">
            <div class="space-y-2">
              <p class="text-sm text-muted">
                Games
              </p>
              <USkeleton class="h-8 w-16 rounded-lg" />
            </div>
            <div class="space-y-2">
              <p class="text-sm text-muted">
                Win rate
              </p>
              <USkeleton class="h-8 w-20 rounded-lg" />
            </div>
          </div>
        </div>
      </div>

      <div class="ml-auto flex w-full max-w-md flex-col items-end gap-4">
        <div class="w-36 space-y-2">
          <p class="text-sm text-default">
            Patch
          </p>
          <USkeleton class="h-10 w-36 rounded-lg" />
        </div>
        <div class="rounded-xl bg-elevated/40 p-1">
          <div class="flex gap-2">
            <div
              v-for="index in 5"
              :key="index"
              class="flex size-8 items-center justify-center rounded-md"
            >
              <USkeleton class="size-5 rounded-sm" />
            </div>
          </div>
        </div>
      </div>
    </header>

    <section class="space-y-4">
      <h2 class="text-xl font-semibold">
        Core
      </h2>
      <UCard variant="subtle">
        <div class="grid gap-4 md:grid-cols-2 xl:grid-cols-5">
          <div class="space-y-3">
            <p class="text-sm font-medium text-muted">
              Starter items
            </p>
            <div class="flex items-center gap-1">
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="size-14 rounded-xl" />
            </div>
          </div>

          <div class="space-y-3">
            <p class="text-sm font-medium text-muted">
              Build path
            </p>
            <div class="flex items-center gap-1">
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="size-14 rounded-xl" />
            </div>
          </div>

          <div class="space-y-3">
            <p class="text-sm font-medium text-muted">
              Boots
            </p>
            <div class="flex items-center gap-1">
              <USkeleton class="size-14 rounded-xl" />
            </div>
          </div>

          <div
            class="space-y-3"
          >
            <p class="text-sm font-medium text-muted">
              Summoners
            </p>
            <div class="flex items-center gap-1">
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="size-14 rounded-xl" />
            </div>
          </div>

          <div class="space-y-3">
            <p class="text-sm font-medium text-muted">
              Skill order
            </p>
            <div class="flex items-center gap-1">
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="size-14 rounded-xl" />
            </div>
          </div>
        </div>
      </UCard>
    </section>

    <section class="space-y-4">
      <h2 class="text-xl font-semibold">
        Advanced details
      </h2>
      <div class="grid gap-4 xl:grid-cols-3">
        <UCard
          v-for="card in 3"
          :key="card"
          variant="subtle"
        >
          <template #header>
            <h3 class="text-base font-semibold">
              {{ ['Summoner options', 'Skill options', 'Starter item options'][card - 1] }}
            </h3>
          </template>

          <div class="space-y-4">
          <div
            v-for="row in 3"
            :key="row"
            class="flex items-center justify-between gap-3"
          >
            <div class="flex items-center gap-1">
              <USkeleton class="size-10 rounded-md" />
              <USkeleton class="size-10 rounded-md" />
              <USkeleton
                v-if="card !== 1"
                class="size-10 rounded-md"
              />
            </div>
            <div class="flex gap-2">
              <USkeleton class="h-7 w-18 rounded-full" />
                <USkeleton class="h-7 w-22 rounded-full" />
              </div>
            </div>
          </div>
        </UCard>
      </div>
    </section>

    <UCard variant="subtle">
      <template #header>
        <div class="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div class="space-y-1">
            <h2 class="text-lg font-semibold">
              Build tree
            </h2>
            <p class="text-sm text-muted">
              Arbre des probabilités d’achat.
            </p>
          </div>
          <div class="flex items-center gap-2">
            <span class="text-sm text-muted">Correlated boots</span>
            <USkeleton class="size-10 rounded-md" />
          </div>
        </div>
      </template>

      <div class="space-y-4">
        <div class="flex flex-wrap items-center gap-3">
          <USkeleton
            v-for="index in 4"
            :key="`tree-root-skeleton-${index}`"
            class="size-10 rounded-md"
          />
        </div>

        <div class="flex justify-center py-8">
        <div class="flex min-w-max flex-col items-center gap-4">
          <USkeleton class="size-14 rounded-xl" />
          <USkeleton class="h-4 w-px" />
          <div class="flex items-start gap-8">
            <div class="flex flex-col items-center gap-4">
              <USkeleton class="h-px w-16" />
              <USkeleton class="size-14 rounded-xl" />
            </div>
            <div class="flex flex-col items-center gap-4">
              <USkeleton class="h-px w-16" />
              <USkeleton class="size-14 rounded-xl" />
              <USkeleton class="h-4 w-px" />
              <div class="flex items-center gap-3">
                <USkeleton class="size-14 rounded-xl" />
                <USkeleton class="size-14 rounded-xl" />
                <USkeleton class="size-14 rounded-xl" />
              </div>
            </div>
          </div>
        </div>
      </div>
      </div>
    </UCard>
  </section>
</template>
