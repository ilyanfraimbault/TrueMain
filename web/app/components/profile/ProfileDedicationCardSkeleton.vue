<template>
  <!-- Same block shape as ProfileDedicationCard.vue (score row + four component
       rows) so the left rail doesn't shift when the profile resolves. -->
  <section class="flex flex-col gap-2" aria-hidden="true">
    <USkeleton class="h-3 w-24" />
    <div class="flex flex-col gap-3 rounded-lg bg-elevated/40 p-3">
      <div class="flex items-center gap-3">
        <div class="flex flex-col gap-1">
          <USkeleton class="h-8 w-16" />
          <USkeleton class="h-3 w-20" />
        </div>
        <div class="ml-auto flex items-center gap-2">
          <USkeleton class="size-8 rounded" />
          <USkeleton class="h-4 w-20" />
        </div>
      </div>
      <div v-for="i in 4" :key="i" class="flex flex-col gap-1">
        <div class="flex items-center justify-between gap-2">
          <USkeleton class="h-3 w-16" />
          <USkeleton class="h-3 w-24" />
        </div>
        <USkeleton class="h-1.5 w-full rounded-full" />
      </div>
    </div>
  </section>
</template>
