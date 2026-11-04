<script setup lang="ts">
import type { PersonaResponse } from '#shared/types/players'
import { PLAYER_PICTURE_FETCH_SIZE } from '#common/utils/icon-fetch'

// A known player's page (#2016): the person — a pro, a streamer, a creator or
// any notable player — their public channels, and every account of theirs
// TrueMain tracks, the one played most recently first, each leading back to the
// regular profile. Accounts sources list but TrueMain does not track are not
// shown: there is no profile to open. Each app's route reads the player
// (`useKnownPlayer`) and hands it here.
const props = defineProps<{
  slug: string
  persona: PersonaResponse | null | undefined
  loading: boolean
  notFound: boolean
  error: unknown
}>()

const { data: versions } = useDDragonVersions()
const patch = computed(() => versions.value?.[0] ?? null)
const { data: championsData } = useChampionStaticList()
const championsById = computed(() => new Map((championsData.value ?? []).map(c => [c.championId, c])))

const breadcrumbItems = computed(() => [
  { label: 'Truemains', to: '/truemains' },
  { label: props.persona?.displayName ?? props.slug },
])
</script>

<template>
  <div class="mx-auto flex w-full max-w-6xl flex-col gap-6 p-4 md:p-6">
    <UBreadcrumb :items="breadcrumbItems" />

    <FetchErrorAlert
      :error="error"
      title="This player could not be loaded"
    />

    <UEmpty
      v-if="notFound"
      icon="i-lucide-user-x"
      title="Unknown player"
      description="No known player answers to this name."
      :actions="[{ label: 'Back to the truemains', to: '/truemains', color: 'neutral', variant: 'subtle' }]"
    />

    <template v-else-if="persona">
      <!-- The person first: their picture, their name, and their channels on
           the name's line. The picture is their first channel's that has one
           (#2446), the placeholder otherwise. -->
      <header class="flex items-center gap-4">
        <SkeletonImage
          v-if="persona.pictureUrl"
          :src="persona.pictureUrl"
          :alt="persona.displayName"
          :fetch-size="PLAYER_PICTURE_FETCH_SIZE"
          class="size-20 shrink-0 rounded-xl ring-1 ring-default"
        />
        <UAvatar
          v-else
          :alt="persona.displayName"
          icon="i-lucide-user-round"
          :ui="{ root: 'size-20 shrink-0 rounded-xl bg-elevated ring-1 ring-default text-2xl', icon: 'size-9 text-dimmed' }"
        />
        <div class="min-w-0 flex-1">
          <p class="eyebrow">
            Player
          </p>
          <div class="flex flex-wrap items-center justify-between gap-x-4 gap-y-2">
            <h1 class="truncate text-2xl font-semibold tracking-tight text-highlighted sm:text-3xl">
              {{ persona.displayName }}
            </h1>
            <PlayersPlayerLinks :links="persona.links" />
          </div>
        </div>
      </header>

      <section class="flex flex-col gap-2">
        <h2 class="eyebrow">
          Accounts
        </h2>
        <ul class="flex flex-col gap-1">
          <PlayersPlayerAccountRow
            v-for="account in persona.accounts"
            :key="`${account.platformId}:${account.gameName}#${account.tagLine}`"
            :account="account"
            :champions-by-id="championsById"
            :patch="patch"
          />
        </ul>
      </section>
    </template>

    <div
      v-else-if="loading"
      class="flex flex-col gap-2"
    >
      <USkeleton class="h-9 w-64" />
      <USkeleton
        v-for="index in 3"
        :key="index"
        class="h-14 rounded-lg"
      />
    </div>
  </div>
</template>
