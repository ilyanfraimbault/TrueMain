<script setup lang="ts">
import type { BuildCoreView } from '~/types/build'

/**
 * The middle of a build view: the rune page as the client's editor draws it —
 * both trees in full, the picked runes lit — and the skill max order under it.
 */
const props = defineProps<{
  core: BuildCoreView
  championId: number
}>()

const { aliasOf } = useChampionStatics()
const { loadChampionSpells, championSpell } = useBuildStatics()

const alias = computed(() => aliasOf(props.championId))
watch(alias, value => value && loadChampionSpells(value), { immediate: true })

const sequence = computed(() => props.core.skillOrder?.sequence ?? [])
</script>

<template>
  <div class="flex flex-col gap-5 p-4">
    <BuildSection title="Runes" :win-rate="core.runePage?.winRate ?? null" :games="core.runePage?.games ?? null">
      <div class="flex justify-center py-2">
        <RuneTree v-if="core.runePage" :page="core.runePage" class="origin-top [zoom:1.25]" />
        <p v-else class="py-10 text-sm text-dimmed">No rune page recorded</p>
      </div>
    </BuildSection>

    <BuildSection title="Skill max order" :win-rate="core.skillOrder?.winRate ?? null" :games="core.skillOrder?.games ?? null">
      <div class="flex items-center gap-2">
        <template v-for="(key, index) in sequence" :key="key">
          <div class="flex items-center gap-2">
            <GameIcon v-if="alias && championSpell(alias, key)" :source="championSpell(alias, key)" size="size-8" />
            <span v-else class="flex size-8 items-center justify-center rounded bg-accented text-sm font-bold">{{ key }}</span>
            <span class="text-sm font-semibold text-highlighted">{{ key }}</span>
          </div>
          <UIcon v-if="index < sequence.length - 1" name="i-lucide-chevron-right" class="size-4 text-dimmed" />
        </template>
        <span v-if="!sequence.length" class="text-sm text-dimmed">No skill order recorded</span>
      </div>
    </BuildSection>
  </div>
</template>
