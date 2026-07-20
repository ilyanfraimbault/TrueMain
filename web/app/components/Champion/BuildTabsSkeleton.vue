<script setup lang="ts">
// Mirrors ChampionBuildTabs + the "Core view" section of ChampionBuildPanel
// (card chrome, tab row, core-view grid) so the placeholder keeps the same
// proportions as the real content and swaps in without a layout shift.
</script>

<template>
  <UCard
    :ui="{ body: 'p-0' }"
    aria-hidden="true"
  >
    <div class="flex border-b border-default">
      <div
        v-for="i in 3"
        :key="i"
        class="flex flex-1 items-center justify-center gap-1.5 px-3 py-2"
      >
        <USkeleton class="size-6 rounded" />
        <USkeleton class="size-6 rounded-full" />
        <USkeleton class="h-3 w-8" />
      </div>
    </div>

    <div class="grid gap-x-6 gap-y-5 p-3 sm:p-4 lg:grid-cols-[minmax(0,1fr)_240px]">
      <div class="flex flex-col gap-5 sm:flex-row sm:items-start">
        <div class="flex flex-col gap-5">
          <div class="flex flex-col gap-2">
            <USkeleton class="h-3 w-16" />
            <div class="flex h-9 w-full items-center gap-1 sm:w-[76px]">
              <USkeleton
                v-for="i in 2"
                :key="i"
                class="size-9 shrink-0 rounded"
              />
            </div>
          </div>
          <div class="flex flex-col gap-2">
            <USkeleton class="h-3 w-16" />
            <div class="flex h-9 w-full items-center gap-1 sm:w-starter-items">
              <USkeleton
                v-for="i in 3"
                :key="i"
                class="size-9 shrink-0 rounded"
              />
            </div>
          </div>
        </div>
        <div class="flex flex-1 flex-col gap-5">
          <div class="flex flex-wrap items-start justify-around gap-6">
            <div class="flex flex-col gap-2">
              <USkeleton class="h-3 w-20" />
              <div class="flex h-12 w-full items-center gap-2 sm:w-[216px]">
                <USkeleton
                  v-for="i in 6"
                  :key="i"
                  class="size-12 shrink-0 rounded-lg"
                />
              </div>
            </div>
            <div class="flex flex-col gap-2">
              <USkeleton class="h-3 w-12" />
              <div class="flex h-9 w-full items-center gap-1 sm:w-[76px]">
                <USkeleton
                  v-for="i in 2"
                  :key="i"
                  class="size-9 shrink-0 rounded"
                />
              </div>
            </div>
          </div>
          <div class="flex justify-center">
            <div class="flex h-9 items-center gap-1 sm:w-build-path">
              <USkeleton
                v-for="i in 6"
                :key="i"
                class="size-9 shrink-0 rounded"
              />
            </div>
          </div>
        </div>
      </div>

      <div class="flex w-full shrink-0 flex-wrap items-stretch gap-x-6 gap-y-4 lg:w-[240px]">
        <div class="flex flex-col items-center gap-1">
          <div class="flex items-center gap-0.5">
            <USkeleton
              v-for="i in 4"
              :key="i"
              class="size-[35px] rounded-full"
            />
          </div>
          <div
            v-for="row in 3"
            :key="row"
            class="flex items-center gap-1"
          >
            <USkeleton
              v-for="i in 3"
              :key="i"
              class="size-6 rounded-full"
            />
          </div>
        </div>
        <div class="flex flex-col justify-between gap-3">
          <div class="flex flex-col items-center gap-1">
            <div
              v-for="row in 2"
              :key="row"
              class="flex items-center gap-1"
            >
              <USkeleton
                v-for="i in 3"
                :key="i"
                class="size-5 rounded-full"
              />
            </div>
          </div>
          <div class="flex items-center gap-1">
            <USkeleton
              v-for="i in 3"
              :key="i"
              class="size-4 rounded-full"
            />
          </div>
        </div>
      </div>
    </div>
  </UCard>
</template>
