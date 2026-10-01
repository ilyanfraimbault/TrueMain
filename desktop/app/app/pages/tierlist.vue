<script setup lang="ts">
import type { ChampionTierListResponse } from '~~/shared/types/champions'
import type { ChampionPosition } from '~/utils/positions'
import { DEFAULT_ELO_BRACKET, normalizeEloBracket } from '~/utils/elo-brackets'

/**
 * The site's tier list (web/app/pages/champions/tierlist.vue): each tier a card
 * of champion portraits badged with their lane, a portrait's tooltip giving its
 * win, pick and ban rate, and the site's filters — lane, rank, truemains only,
 * patch. A portrait opens the champion's builds on that lane. The champion
 * table lives on the Champions page.
 */
const lane = ref<ChampionPosition | null>(null)
const eloBracket = ref(DEFAULT_ELO_BRACKET)
const truemainsOnly = ref(true)
const patch = ref<string | null>(null)

const tierList = ref<ChampionTierListResponse | null>(null)
const status = ref<'pending' | 'ready' | 'error'>('pending')
let latest = 0

async function load() {
  const id = ++latest
  status.value = 'pending'
  try {
    const answer = await apiGet<ChampionTierListResponse>('/champions/tierlist', {
      patch: patch.value,
      position: lane.value,
      eloBracket: eloBracket.value,
      truemainsOnly: truemainsOnly.value ? null : 'false',
    })
    if (id !== latest) return
    tierList.value = answer
    status.value = 'ready'
  }
  catch {
    if (id === latest) status.value = 'error'
  }
}
watch([lane, eloBracket, truemainsOnly, patch], load, { immediate: true })

/** The patch served and the four before it: the site keeps every patch's tier list. */
const patchOptions = computed(() => {
  const served = tierList.value?.patchVersion
  const match = served?.match(/^(\d+)\.(\d+)$/)
  if (!match) return served ? [served] : []
  const [major, minor] = [Number(match[1]), Number(match[2])]
  const current = patch.value ?? served!
  const list = Array.from({ length: 5 }, (_, back) => minor - back).filter(value => value > 0).map(value => `${major}.${value}`)
  return list.includes(current) ? list : [current, ...list]
})
const selectedPatch = computed({
  get: () => patch.value ?? tierList.value?.patchVersion ?? undefined,
  set: (value: string | undefined) => (patch.value = value ?? null),
})

const { nameOf, portraitOf } = useChampionStatics()
const groups = computed(() => (tierList.value?.tiers ?? []).filter(group => group.entries.length))
</script>

<template>
  <div class="flex h-full flex-col gap-4 overflow-y-auto p-6">
    <PageHeader title="Tier list" icon="i-lucide-trending-up" />

    <div class="flex flex-wrap items-center justify-between gap-3">
      <RolePicker v-model:position="lane" />
      <ChampionEloFilter :model-value="normalizeEloBracket(eloBracket)" size="sm" @update:model-value="eloBracket = $event" />
      <ChampionTruemainToggle v-model="truemainsOnly" />
      <USelect v-model="selectedPatch" :items="patchOptions" placeholder="Patch" size="sm" class="w-24" />
    </div>

    <TierlistSkeleton v-if="status === 'pending' && !tierList" />
    <p v-else-if="status === 'error'" class="p-8 text-center text-sm text-muted">The tier list could not be loaded</p>
    <div v-else class="flex flex-col gap-3" :class="status === 'pending' ? 'opacity-60 transition-opacity' : ''">
      <SectionCard v-for="group in groups" :key="group.tier">
        <template #title>
          <div class="flex items-center gap-2">
            <TierBadge :tier="group.tier" />
            <span class="text-xs text-muted">{{ group.entries.length }} champions</span>
          </div>
        </template>
        <ul class="flex flex-wrap gap-3">
          <li v-for="entry in group.entries" :key="`${entry.championId}-${entry.position}`">
            <ChampionTierChip
              :to="`/champions/${entry.championId}?lane=${entry.position}`"
              :name="nameOf(entry.championId)"
              :icon-url="portraitOf(entry.championId) ?? ''"
              :position="entry.position"
              :win-rate="entry.winRate"
              :pick-rate="entry.pickRate"
              :ban-rate="entry.banRate"
            />
          </li>
        </ul>
      </SectionCard>
      <p v-if="!groups.length" class="text-sm text-muted">No champions match these filters.</p>
    </div>
  </div>
</template>
