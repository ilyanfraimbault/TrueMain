<script setup lang="ts">
// Detailed placeholder for the expanding match-detail panel. Mirrors the
// General tab's layout — the tab bar plus two team scoreboards of five rows
// each — closely enough (same paddings, icon sizes and column rhythm as
// MatchDetailScoreboard) that the accordion opens straight to roughly the
// loaded height. The real content then swaps in without the row jumping,
// instead of the panel popping open at a tiny "loading" box and lurching to
// full size once the fetch resolves. Purely decorative, so aria-hidden.
</script>

<template>
  <div aria-hidden="true" class="flex flex-col gap-3">
    <!-- Tab bar (General · Details · Runes) -->
    <div class="flex gap-4 border-b border-default/40 pb-2">
      <USkeleton class="h-4 w-16" />
      <USkeleton class="h-4 w-14" />
      <USkeleton class="h-4 w-12" />
    </div>

    <!-- Two team scoreboards -->
    <div
      v-for="team in 2"
      :key="`sk-team-${team}`"
      class="glass overflow-hidden rounded-md border border-default/60 bg-elevated/60"
    >
      <!-- Team header: side + result on the left, aggregates on the right -->
      <div class="flex items-center justify-between px-3 py-2">
        <USkeleton class="h-3 w-32" />
        <USkeleton class="h-3 w-20" />
      </div>

      <!-- Five participant rows -->
      <ul class="divide-y divide-default/40">
        <li
          v-for="row in 5"
          :key="`sk-row-${team}-${row}`"
          class="flex items-center gap-2 px-3 py-2"
        >
          <!-- Champion + level -->
          <USkeleton class="size-10 shrink-0 rounded" />
          <!-- Summoner spells -->
          <div class="flex shrink-0 flex-col gap-0.5">
            <USkeleton class="size-[18px] rounded" />
            <USkeleton class="size-[18px] rounded" />
          </div>
          <!-- Keystone + secondary tree -->
          <div class="flex shrink-0 flex-col items-center gap-0.5">
            <USkeleton class="size-[18px] rounded-full" />
            <USkeleton class="size-[14px] rounded-full" />
          </div>
          <!-- Identity + rank -->
          <div class="flex min-w-0 flex-[1.2] flex-col gap-1">
            <USkeleton class="h-3 w-24" />
            <USkeleton class="h-2.5 w-16" />
          </div>
          <!-- KDA -->
          <div class="flex w-[4.5rem] shrink-0 flex-col items-center gap-1">
            <USkeleton class="h-3 w-14" />
            <USkeleton class="h-2.5 w-10" />
          </div>
          <!-- CS + KP -->
          <div class="flex w-[3.5rem] shrink-0 flex-col items-center gap-1">
            <USkeleton class="h-2.5 w-10" />
            <USkeleton class="h-2.5 w-8" />
          </div>
          <!-- Damage bar -->
          <div class="hidden w-[5rem] shrink-0 flex-col gap-1 sm:flex">
            <USkeleton class="h-2.5 w-12" />
            <USkeleton class="h-1.5 w-full rounded-full" />
          </div>
          <!-- Items + trinket -->
          <div class="ml-auto hidden shrink-0 items-center gap-1 md:flex">
            <div class="flex gap-0.5">
              <USkeleton
                v-for="i in 6"
                :key="`sk-inv-${team}-${row}-${i}`"
                class="size-5 rounded"
              />
            </div>
            <USkeleton class="size-5 rounded-full" />
          </div>
        </li>
      </ul>
    </div>
  </div>
</template>
