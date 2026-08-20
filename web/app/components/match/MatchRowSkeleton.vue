<script setup lang="ts">
// Mirrors MatchRow.vue's column shape so the skeleton placeholders line up
// visually with the eventual content — height, padding and gap match the
// real row to avoid layout shift on resolve. That includes its @container
// tiers: the skeleton has to shrink in a drawer exactly where the row does,
// or the list jumps a size on resolve.
</script>

<template>
  <article
    class="@container flex overflow-hidden rounded-md bg-elevated"
    aria-hidden="true"
  >
    <div class="flex flex-1 flex-wrap items-center gap-2 px-2 py-2 @md:flex-nowrap @2xl:gap-3 @2xl:px-3 @2xl:py-2.5">
      <!-- Champion portrait. No meta column: the real row dropped its
           result label, and its duration moved into the stats cluster. -->
      <USkeleton class="size-10 shrink-0 rounded @2xl:ml-1 @2xl:size-12" />
      <!-- KDA + stats -->
      <div class="flex w-28 shrink-0 items-center gap-2 @3xl:w-52 @3xl:gap-3">
        <div class="flex w-28 flex-col items-center gap-1">
          <USkeleton class="h-5 w-20" />
          <USkeleton class="h-3 w-14" />
          <USkeleton class="h-3 w-10 @3xl:hidden" />
        </div>
        <div class="hidden flex-1 flex-col items-center gap-0.5 @3xl:flex">
          <USkeleton class="h-3 w-14" />
          <USkeleton class="h-3 w-12" />
          <USkeleton class="h-3 w-12" />
          <USkeleton class="h-3 w-10" />
        </div>
      </div>
      <!-- Loadout strip: summoners + runes + item block -->
      <div class="order-last flex w-full grow items-center justify-center @md:order-none @md:w-auto @md:flex-1">
        <div class="flex shrink-0 items-center gap-1">
          <div class="flex flex-col gap-0.5">
            <USkeleton class="size-[18px] rounded @2xl:size-[22px]" />
            <USkeleton class="size-[18px] rounded @2xl:size-[22px]" />
          </div>
          <div class="flex flex-col items-center gap-0.5">
            <USkeleton class="size-[18px] rounded-full @2xl:size-[22px]" />
            <USkeleton class="size-[15px] rounded-full @2xl:size-[18px]" />
          </div>
          <div class="flex items-center gap-1 rounded-lg bg-black/25 p-1 @2xl:gap-1.5 @2xl:p-1.5">
            <div class="grid grid-cols-3 gap-0.5 @2xl:gap-1">
              <USkeleton v-for="i in 6" :key="`inv-${i}`" class="size-5 rounded @2xl:size-6" />
            </div>
            <div class="flex flex-col gap-0.5 @2xl:gap-1">
              <USkeleton class="size-5 rounded-full @2xl:size-6" />
              <USkeleton class="size-5 rounded @2xl:size-6" />
            </div>
          </div>
        </div>
      </div>
      <!-- Composition rows placeholder — two horizontal rows of 5 so the
           skeleton keeps the same height as the resolved row. -->
      <div class="ml-auto flex shrink-0 items-center gap-2 @md:ml-0 @2xl:gap-3">
        <div class="hidden shrink-0 flex-col gap-0.5 @xl:flex">
          <div class="flex gap-0.5">
            <USkeleton v-for="i in 5" :key="`ally-${i}`" class="size-6 rounded" />
          </div>
          <div class="flex gap-0.5">
            <USkeleton v-for="i in 5" :key="`enemy-${i}`" class="size-6 rounded" />
          </div>
        </div>
        <USkeleton class="size-5 shrink-0 rounded-full" />
      </div>
    </div>
  </article>
</template>
