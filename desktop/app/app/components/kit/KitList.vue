<script setup lang="ts">
import type { Kit } from '#shared/types/kit'
import { POSITION_BY_VALUE } from '#common/utils/positions'
import { kitPaths } from '~/utils/kit-tree'

/**
 * The player's kits, as rows in the build list's style (`BuildList`): the
 * name, the keystone, the spells and the default path as icons, a kit used in
 * champion select marked "Active" — and, on the My kits page (#2205), the
 * positions each is offered on.
 */
withDefaults(defineProps<{ kits: Kit[], activeIds: string[], showPositions?: boolean }>(), { showPositions: false })
const selected = defineModel<string | null>({ required: true })

const { items, summoners, runeTree } = useStaticData()

const firstPath = (kit: Kit) => kitPaths(kit.tree)[0] ?? []
</script>

<template>
  <ul class="space-y-1" aria-label="Your kits">
    <li v-for="kit in kits" :key="kit.id">
      <button
        type="button"
        class="relative w-full rounded-lg px-2.5 py-2 text-left transition-colors"
        :class="selected === kit.id ? 'bg-accented' : 'hover:bg-elevated'"
        :aria-current="selected === kit.id ? 'true' : undefined"
        @click="selected = kit.id"
      >
        <span v-if="selected === kit.id" class="absolute inset-y-2 left-0 w-0.5 rounded-full bg-primary" />
        <span class="flex items-center gap-2">
          <span class="min-w-0 flex-1 truncate text-sm font-medium text-highlighted">{{ kit.name }}</span>
          <img
            v-for="position in showPositions ? kit.positions : []"
            :key="position"
            :src="POSITION_BY_VALUE.get(position)?.iconUrl"
            :alt="POSITION_BY_VALUE.get(position)?.label ?? position"
            :title="POSITION_BY_VALUE.get(position)?.label ?? position"
            width="14"
            height="14"
            class="size-3.5 opacity-70"
          >
          <UBadge v-if="activeIds.includes(kit.id)" label="Active" color="primary" variant="soft" size="sm" />
        </span>
        <span class="mt-1.5 flex items-center gap-1.5">
          <GameTooltipPerkIcon :perk="runeTree?.perks[kit.runes.primaryKeystoneId] ?? null" :width="22" :height="22" class="size-[22px] rounded-full" />
          <img
            v-for="spellId in kit.spells"
            :key="spellId"
            :src="summoners[spellId]?.iconUrl"
            :alt="summoners[spellId]?.name ?? ''"
            width="18"
            height="18"
            class="size-[18px] rounded-sm img-skeleton"
          >
          <span class="mx-0.5 h-4 w-px bg-default" aria-hidden="true" />
          <img
            v-for="(itemId, index) in firstPath(kit).slice(0, 6)"
            :key="`${itemId}-${index}`"
            :src="items[itemId]?.iconUrl"
            :alt="items[itemId]?.name ?? ''"
            width="20"
            height="20"
            class="size-5 rounded-sm img-skeleton"
          >
        </span>
      </button>
    </li>
  </ul>
</template>
