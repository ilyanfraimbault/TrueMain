<script setup lang="ts">
import type { ProfileIdentity } from '~~/shared/types/profile'
import { platformIdToRegion } from '~~/shared/utils/region'

const props = defineProps<{
  identity: ProfileIdentity
  patch: string | null
  /**
   * Stack the icon over the identity from `xl` up, for the narrow left rail of
   * the player-scoped champion page. The default row layout keeps its full
   * width on the profile page; in a ~17rem rail it would squeeze the Riot ID
   * into three wrapped lines beside the icon. Below `xl` the rail is full width
   * again, so the row layout is the right one there in both cases.
   */
  stacked?: boolean
}>()

const region = computed(() => platformIdToRegion(props.identity.platformId))

const tag = computed(() => (props.identity.tagLine ? `#${props.identity.tagLine}` : ''))

/**
 * Riot IDs run up to 16 + 5 characters, which at `text-2xl` is wider than the
 * rail. The title wraps before the `#`, so the widest line is whichever part is
 * longer; the font then shrinks until that line fits the name block's width
 * (`100cqi`) minus the follow star and its gap (2.25rem). 0.62em is Inter
 * semibold's average advance for ordinary IDs, measured; wide-glyph outliers
 * (`WWWW…`) still fit through the `overflow-wrap: anywhere` fallback.
 */
const titleStyle = computed(() => {
  const chars = Math.max(props.identity.gameName.length, tag.value.length, 1)
  return { fontSize: `clamp(1rem, (100cqi - 2.25rem) / ${(chars * 0.62).toFixed(2)}, 1.5rem)` }
})
</script>

<template>
  <!-- The shared identity (#1734) at hero size, without a link — this is the
       profile. Its body is an inline-size container so the title can size
       itself against the room actually left beside (or, stacked, under) the
       icon; containment drops content-based sizing, hence `flex-1` and the
       stacked `self-stretch`. -->
  <Account
    as="section"
    :identity="identity"
    :patch="patch"
    size="xl"
    :to="false"
    :ui="{
      root: stacked ? 'xl:flex-col xl:items-start xl:gap-3' : undefined,
      body: `@container flex flex-1 flex-col items-start gap-1 ${stacked ? 'xl:self-stretch' : ''}`,
      subline: 'flex-wrap gap-2 text-sm text-muted',
    }"
  >
    <!-- The follow star sits on the Riot ID, not under the identity block: it
         acts on the account the title names, and as a labelled pill on its own
         line it read as a third stat under the level rather than as a control
         attached to the name. -->
    <template #name>
      <div class="flex min-w-0 items-center gap-2">
        <h1
          class="min-w-0 font-semibold leading-tight [overflow-wrap:anywhere]"
          :style="titleStyle"
        >
          {{ identity.gameName }}<wbr>{{ tag }}
        </h1>
        <FavoriteToggle
          :game-name="identity.gameName"
          :tag-line="identity.tagLine"
          :region="region"
          :profile-icon-id="identity.profileIconId"
        />
      </div>
    </template>
    <template #subline>
      <span>Level {{ identity.summonerLevel }}</span>
    </template>
  </Account>
</template>
