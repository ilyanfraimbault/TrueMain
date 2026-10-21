<script setup lang="ts">
import { parseRouteParam } from '#common/utils/route-params'
import { platformIdToRegion } from '#shared/utils/region'
import { riotIdLabel, truemainSlugLabel } from '#shared/utils/truemain-path'

/**
 * Another player's profile, in the app (#1983): the site's
 * `/truemains/{nameTag}` on the same shared page (`PagePlayerProfile`) and the
 * same reads, so a name clicked anywhere in the app opens here rather than in
 * the browser. TrueMain is the only source — the client knows only its own
 * player — so the banner is the champion they truemain and the history is
 * TrueMain's copy, paged. The main-champions card stays on the site for now.
 */
const route = useRoute()
const nameTag = computed(() => parseRouteParam(route.params.nameTag))

const page = ref(1)
watch(nameTag, () => (page.value = 1))

const { data: profile, isInitialLoading: profileLoading, notFound: profileNotFound } = useTruemainProfile(nameTag)
const { data: rankHistory, isInitialLoading: rankHistoryLoading } = useTruemainRankHistory(nameTag)
const apexCutoffs = useApexCutoffs(() => profile.value?.identity.platformId)
const { data: activity, isInitialLoading: activityLoading } = useTruemainActivity(nameTag)
const { matches, total, pageSize, isInitialLoading: matchesLoading, notFound: matchesNotFound } = useTruemainMatches(nameTag, page, { pageSize: 20 })

const championsById = useStaticChampionsById()
const champions = computed(() => [...championsById.value.values()])
const { items, summoners, runeTree } = useStaticData()
const { aliasOf, patch } = useChampionStatics()

const truemainId = computed(() => profile.value?.dedication?.championId ?? profile.value?.mains[0]?.championId ?? null)
const truemain = computed(() => {
  const champion = truemainId.value ? championsById.value.get(truemainId.value) : null
  return champion ? { name: champion.name, iconUrl: champion.iconUrl } : null
})
const splashUrl = computed(() => {
  const alias = truemainId.value ? aliasOf(truemainId.value) : null
  return alias ? splashOfAlias(alias) : null
})
// The player before the profile lands: the slug's Riot ID, without a picture.
const identity = computed(() => profile.value?.identity ?? { gameName: truemainSlugLabel(nameTag.value), tagLine: null })
</script>

<template>
  <div class="flex h-full flex-col gap-4 overflow-y-auto p-5">
    <UEmpty
      v-if="profileNotFound"
      icon="i-lucide-user-x"
      title="Player not found"
      description="TrueMain does not track this Riot ID."
      class="py-12"
    />

    <PagePlayerProfile
      v-else
      :identity="identity"
      :patch="patch || null"
      :level="profile?.identity.summonerLevel ?? null"
      :ranked="profile?.ranked ?? null"
      :truemain="truemain"
      :splash-url="splashUrl"
      splash-position="62% 22%"
      :matches="matches"
      :loading="matchesLoading"
    >
      <template #header>
        <UBreadcrumb :items="[{ label: 'Truemains', to: '/truemains' }, { label: riotIdLabel(identity.gameName, identity.tagLine) }]" />
      </template>

      <template v-if="profile" #title-actions>
        <FavoriteToggle
          :game-name="profile.identity.gameName"
          :tag-line="profile.identity.tagLine"
          :region="platformIdToRegion(profile.identity.platformId)"
          :profile-icon-id="profile.identity.profileIconId"
        />
      </template>

      <template #history>
        <h2 class="text-xs font-semibold uppercase tracking-wide text-muted">Match history</h2>
        <MatchHistoryList
          v-model:page="page"
          :matches="matches"
          :loading="matchesLoading"
          :total="total"
          :page-size="pageSize"
          :champions="champions"
          :items="items"
          :summoner-spells="summoners"
          :rune-tree="runeTree"
          :name-tag="nameTag"
          :not-found="matchesNotFound"
        />
      </template>

      <template #aside>
        <ProfileRankedCardSkeleton v-if="profileLoading || !profile" />
        <ProfileRankedCard
          v-else
          :ranked="profile.ranked"
          :history="rankHistory?.entries ?? []"
          :history-loading="rankHistoryLoading || apexCutoffs.loading"
          :cutoffs="apexCutoffs.cutoffs"
        />
        <ProfileActivityHeatmap
          v-if="profile"
          :data="activity"
          :loading="activityLoading"
        />
        <ProfileDedicationCardSkeleton v-if="profileLoading || !profile" />
        <ProfileDedicationCard
          v-else-if="profile.dedication"
          :dedication="profile.dedication"
          :champions="champions"
          :name-tag="nameTag"
        />
        <ProfilePositionBreakdownSkeleton v-if="profileLoading || !profile" />
        <ProfilePositionBreakdown
          v-else
          :positions="profile.positions"
        />
      </template>
    </PagePlayerProfile>
  </div>
</template>
