<script setup lang="ts">
import type { Kit } from '#shared/types/kit'
import { kitCoreView } from '~/utils/kit'

/**
 * A kit as the build view draws a build (#2185): the site's core blocks —
 * summoners, starter, boots, the rune page — over the kit's own tree, each
 * with its import button in its corner while the client is there: the rune
 * page and the item set as a build's are written, the spells into the
 * running champion select. Every write is that button's click.
 */
const props = defineProps<{ kit: Kit }>()

const { items, summoners, runeTree, pending } = useStaticData()
const { nameOf } = useChampionStatics()
const { available, inChampSelect, pending: importing, importItems, importSpells } = useKitImport()

const core = computed(() => kitCoreView(props.kit))
const champion = computed(() => nameOf(props.kit.championId))
</script>

<template>
  <div class="space-y-4">
    <BuildCore
      :summoner-spells="core.summonerSpells"
      :starter-items="core.starterItems"
      :skill-order="null"
      :boots="core.boots"
      :rune-page="core.runePage"
      :champion-static="null"
      :items-map="items"
      :summoners-map="summoners"
      :summoners-pending="pending"
      :rune-tree="runeTree"
      :champion-name="champion || null"
    >
      <template v-if="inChampSelect" #spells>
        <UTooltip text="Set these spells in champion select" :content="{ side: 'right' }">
          <UButton
            size="xs"
            color="neutral"
            variant="ghost"
            square
            icon="i-lucide-download"
            :loading="importing === 'spells'"
            aria-label="Set these spells in champion select"
            class="text-dimmed hover:text-highlighted"
            @click="importSpells(kit)"
          />
        </UTooltip>
      </template>
    </BuildCore>

    <div class="relative">
      <UTooltip v-if="available" text="Import items into the client" :content="{ side: 'left' }">
        <UButton
          size="xs"
          color="neutral"
          variant="ghost"
          square
          icon="i-lucide-download"
          :loading="importing === 'items'"
          aria-label="Import items into the client"
          class="absolute right-0 top-0 z-10 text-dimmed hover:text-highlighted"
          @click="importItems(kit, champion)"
        />
      </UTooltip>
      <KitTree :tree="kit.tree" :items="items" :boots="kit.boots" :size="28" />
    </div>
  </div>
</template>
