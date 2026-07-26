<script setup lang="ts">
// Mirrors MatchRow.vue's column shape so the skeleton placeholders line up
// visually with the eventual content — height, padding and gap match the
// real row to avoid layout shift on resolve.
</script>

<template>
  <article
    class="flex overflow-hidden rounded-md bg-elevated/30"
    aria-hidden="true"
  >
    <div class="flex flex-1 items-center gap-3 px-3 py-2.5">
      <!-- Meta: result + duration -->
      <div class="flex w-[3.5rem] shrink-0 flex-col gap-1">
        <USkeleton class="h-3 w-12" />
        <USkeleton class="h-3 w-10" />
      </div>
      <!-- Champion portrait -->
      <USkeleton class="ml-1 size-12 shrink-0 rounded" />
      <!-- KDA + stats -->
      <div class="flex min-w-[5.5rem] flex-col items-center gap-1">
        <USkeleton class="h-5 w-20" />
        <USkeleton class="h-3 w-14" />
      </div>
      <div class="flex shrink-0 flex-col gap-0.5">
        <USkeleton class="h-3 w-16" />
        <USkeleton class="h-3 w-12" />
      </div>
      <!-- Loadout strip: summoners + runes + item block -->
      <div class="ml-2 flex shrink-0 items-center gap-1">
        <div class="flex flex-col gap-0.5">
          <USkeleton class="size-[22px] rounded" />
          <USkeleton class="size-[22px] rounded" />
        </div>
        <div class="flex flex-col items-center gap-0.5">
          <USkeleton class="size-[22px] rounded-full" />
          <USkeleton class="size-[18px] rounded-full" />
        </div>
        <div class="flex items-center gap-1.5 rounded-lg bg-black/25 p-1.5">
          <div class="grid grid-cols-3 gap-1">
            <USkeleton v-for="i in 6" :key="`inv-${i}`" class="size-6 rounded" />
          </div>
          <div class="flex flex-col gap-1">
            <USkeleton class="size-6 rounded-full" />
            <USkeleton class="size-6 rounded" />
          </div>
        </div>
      </div>
      <!-- Composition rows placeholder — two horizontal rows of 5 so the
           skeleton keeps the same height as the resolved row. -->
      <div class="ml-2 hidden shrink-0 flex-col gap-0.5 xl:flex">
        <div class="flex gap-0.5">
          <USkeleton v-for="i in 5" :key="`ally-${i}`" class="size-6 rounded" />
        </div>
        <div class="flex gap-0.5">
          <USkeleton v-for="i in 5" :key="`enemy-${i}`" class="size-6 rounded" />
        </div>
      </div>
      <div class="ml-auto shrink-0">
        <USkeleton class="size-5 rounded-full" />
      </div>
    </div>
  </article>
</template>
