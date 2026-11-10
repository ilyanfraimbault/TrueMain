<script setup lang="ts">
import { parseRouteParam } from '#common/utils/route-params'
import { truemainSlugLabel } from '#shared/utils/truemain-path'

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

const { data: profile, isInitialLoading: profileLoading, notFound: profileNotFound, error: profileError } = useTruemainProfile(nameTag)
const { data: rankHistory, isInitialLoading: rankHistoryLoading } = useTruemainRankHistory(nameTag)
const { data: activity, isInitialLoading: activityLoading } = useTruemainActivity(nameTag)
const { matches, total, pageSize, isInitialLoading: matchesLoading, notFound: matchesNotFound, error: matchesError } = useTruemainMatches(nameTag, page, { pageSize: 20 })

const championsById = useStaticChampionsById()
const champions = computed(() => [...championsById.value.values()])
const { items, summoners, runeTree } = useStaticData()
const { aliasOf, patch } = useChampionStatics()

const truemainId = computed(() => profile.value?.dedication?.championId ?? profile.value?.mains[0]?.championId ?? null)
const splashUrl = computed(() => {
  const alias = truemainId.value ? aliasOf(truemainId.value) : null
  return alias ? splashOfAlias(alias) : null
})
// The player before the profile lands: the slug's Riot ID, without a picture.
const identity = computed(() => profile.value?.identity ?? { gameName: truemainSlugLabel(nameTag.value), tagLine: null })
const playerLabel = useTruemainPlayerLabel(nameTag, profile)
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
      :persona="profile?.persona ?? null"
      :splash-url="splashUrl"
      splash-position="62% 22%"
      :matches="matches"
      :loading="matchesLoading"
    >
      <template #header>
        <UBreadcrumb :items="[{ label: 'Truemains', to: '/truemains' }, { label: playerLabel }]" />
      </template>

      <template v-if="profile" #title-actions>
        <ProfileFollowToggle :identity="profile.identity" />
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
          :error="matchesError"
        />
      </template>

      <template #aside>
        <ProfileAside
          :name-tag="nameTag"
          :profile="profile"
          :profile-loading="profileLoading"
          :profile-error="profileError"
          :rank-history="rankHistory?.entries ?? []"
          :rank-history-loading="rankHistoryLoading"
          :activity="activity"
          :activity-loading="activityLoading"
          :champions="champions"
        />
      </template>
    </PagePlayerProfile>
  </div>
</template>
