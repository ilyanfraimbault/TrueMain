<script setup lang="ts">
// Same column shape as LeaderboardRow.vue so placeholders don't shift the
// layout when the data resolves — including the same @container breakpoints.
// The shell is `surface`, the same material the real row gets from
// ListRowSurface: a translucent stand-in reads as a *dimmer* row, so the whole
// list brightened when the data landed. Radius, padding and gap were drifting
// from ListRowSurface too (rounded-md/py-2/gap-3 against rounded-lg/py-2.5/
// gap-2), which is a 4px height shift per row — realigned here.
</script>

<template>
  <div
    class="surface @container flex items-center gap-1.5 rounded-lg px-3 py-2.5"
    aria-hidden="true"
  >
    <USkeleton class="h-4 w-6 @xl:w-8" />
    <USkeleton class="size-5 rounded-full" />
    <USkeleton class="size-10 rounded" />
    <div class="min-w-0 flex-1 space-y-1">
      <USkeleton class="h-4 w-32" />
      <USkeleton class="h-3 w-16" />
    </div>
    <div class="w-32 shrink-0 space-y-1 text-right">
      <USkeleton class="ml-auto h-4 w-20" />
      <USkeleton class="ml-auto h-3 w-12" />
    </div>
    <div class="hidden shrink-0 items-center gap-1 @2xl:flex">
      <USkeleton class="size-7 rounded" />
      <USkeleton class="size-7 rounded" />
      <USkeleton class="size-7 rounded" />
    </div>
    <!-- Dedication slot — reserved at every width, like the real row. -->
    <div class="flex w-14 shrink-0 flex-col items-end gap-1 @xl:w-16">
      <USkeleton class="h-4 w-8" />
      <USkeleton class="h-3 w-14" />
    </div>
    <div class="ml-auto hidden shrink-0 items-center gap-5 @xl:flex">
      <div class="flex w-12 flex-col items-end gap-1">
        <USkeleton class="h-4 w-10" />
        <USkeleton class="h-3 w-8" />
      </div>
      <div class="flex w-12 flex-col items-end gap-1">
        <USkeleton class="h-4 w-10" />
        <USkeleton class="h-3 w-8" />
      </div>
      <div class="flex w-12 flex-col items-end gap-1">
        <USkeleton class="h-4 w-10" />
        <USkeleton class="h-3 w-8" />
      </div>
    </div>
    <!-- Follow toggle slot — reserved on every breakpoint, like the real row. -->
    <USkeleton class="size-7 shrink-0 rounded-md" />
  </div>
</template>
