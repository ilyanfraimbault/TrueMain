<template>
  <section class="flex items-center gap-4" aria-hidden="true">
    <USkeleton class="size-20 rounded-lg" />
    <div class="flex flex-col gap-2">
      <USkeleton class="h-7 w-48" />
      <div class="flex gap-2">
        <USkeleton class="h-4 w-14 rounded-full" />
        <USkeleton class="h-4 w-20" />
      </div>
      <!-- Reserves the follow toggle's box so resolving the profile doesn't
           shove the rest of the left rail down. -->
      <USkeleton class="mt-1 h-8 w-24 rounded-md" />
    </div>
  </section>
</template>
