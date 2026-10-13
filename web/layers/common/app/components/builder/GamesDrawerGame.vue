<script setup lang="ts">
import type { CompositionGame, CompositionGamePilot } from '#shared/types/composition'
import type {
  ChampionStaticListItem,
  RuneTreeResponse,
  StaticItemData,
  StaticSummonerSpellData,
} from '#shared/types/static-data'
import { truemainNameTag } from '#shared/utils/truemain-path'

// One game of the provenance drawer: who piloted it, then the plain
// match-history row.
defineProps<{
  game: CompositionGame
  champions: ChampionStaticListItem[]
  items: Record<number, StaticItemData>
  summonerSpells: Record<number, StaticSummonerSpellData>
  runeTree: RuneTreeResponse | null
  /** Current Data Dragon version for the pilot's profile icon — see `BuilderGamesDrawer`. */
  iconPatch: string | null
}>()

// Route slug of the pilot's profile — the same `name-tag` form the truemain
// routes use, and the identity the row's detail fetch
// (`/truemains/{nameTag}/matches/{matchId}`) is scoped by. So one value both
// links the pilot's profile and makes the row expandable; without a resolved
// Riot account the row degrades to a static, non-expandable article.
function pilotNameTag(pilot: CompositionGamePilot | null): string | null {
  if (!pilot) return null
  return truemainNameTag(pilot.gameName, pilot.tagLine)
}

// A game whose pilot the API could not resolve still gets the chip, unlinked,
// with the user glyph (`Account` draws it for icon id 0, the API's "never
// resolved" marker — there is no such asset, so it is never requested).
const UNKNOWN_PILOT = { gameName: 'Unknown player', tagLine: null, profileIconId: 0 }
</script>

<template>
  <div class="flex flex-col gap-1">
    <!-- Pilot + score strip: sits above the row so the row itself stays
         the unmodified match-history component (which sizes itself off
         the drawer's width — it's an @container). The pilot links to
         their profile; no "Main" badge — every game here comes from a
         player the selection picked for this champion, so the badge
         labelled the majority of the list and told the reader nothing. -->
    <div class="flex items-center gap-2 px-0.5 text-xs text-muted">
      <Account
        :identity="game.pilot ?? UNKNOWN_PILOT"
        :region="null"
        :patch="iconPatch"
        size="xs"
        layout="inline"
        :to="game.pilot ? undefined : false"
      />
    </div>
    <!-- `name-tag` makes the row an accordion over the pilot's slice of
         the game: the detail endpoint is scoped by whoever played it,
         and here that's the pilot, not the visiting user. -->
    <MatchRow
      :match="game.match"
      :champions="champions"
      :items="items"
      :summoner-spells="summonerSpells"
      :rune-tree="runeTree ?? { styles: [], perks: {}, perkStyles: {}, shardSlots: [] }"
      :name-tag="pilotNameTag(game.pilot)"
    />
  </div>
</template>
