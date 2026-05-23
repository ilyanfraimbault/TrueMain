<template>
  <section class="flex flex-col gap-3" aria-hidden="true">
    <USkeleton class="h-3 w-36" />
    <USkeleton class="h-2 w-full rounded-full" />
    <div class="grid grid-cols-5 gap-2">
      <div v-for="i in 5" :key="i" class="flex flex-col items-center gap-0.5">
        <USkeleton class="size-5 rounded" />
        <USkeleton class="h-3 w-8" />
      </div>
    </div>
  </section>
</template>
