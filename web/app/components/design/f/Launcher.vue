<script setup lang="ts">
import { isNavigationFailure } from 'vue-router'
import { formatCompactCount } from '~~/shared/utils/counts'
import type { ChampionOverviewReadModel as ChampionOverviewResponse, ChampionOverviewRowReadModel as ChampionOverviewRow } from '#shared/contract/api'
import type { ChampionStaticListItem } from '~~/shared/types/static-data'
import { matchChampions, toWallChampion } from './champion-filter'
import { DOCK_SEARCH_KEY, dialogOpen, isEditableTarget } from './search-context'

// Variant F ("Dock") — the home's first screen is a search field and the wall
// it filters. Typing anywhere on the page lands in the field; the wall narrows
// to the champions that match as you type, best match first and highlighted;
// Enter opens it, the arrows move through the wall, Escape clears. A name that
// is no champion goes to the player search (⌘K) with the term carried over.
const props = defineProps<{
  overview: ChampionOverviewResponse | null | undefined
  overviewPending: boolean
  championsById: Map<number, ChampionStaticListItem>
  gamesAnalyzed: number
  trackedTruemains: number | null
}>()

const router = useRouter()
const { pathFor } = useChampionSlugs()
const search = inject(DOCK_SEARCH_KEY, null)

const term = ref('')
const focused = ref(false)
const highlight = ref<number | null>(null)
const inputEl = ref<HTMLInputElement | null>(null)
const wall = ref<{ move: (key: string) => boolean } | null>(null)

const champions = computed(() => [...props.championsById.values()]
  .map(toWallChampion)
  .sort((a, b) => a.name.localeCompare(b.name, 'en')))
const wallPending = computed(() => champions.value.length === 0)

const filtering = computed(() => term.value.trim().length > 0)
const matches = computed(() => matchChampions(champions.value, term.value))

/** The patch's most played and banned (first row per champion: the list is pre-sorted). */
const hot = computed(() => {
  const map = new Map<number, ChampionOverviewRow>()
  for (const row of props.overview?.topRows ?? []) if (!map.has(row.championId)) map.set(row.championId, row)
  return map
})

watch(matches, (list) => {
  highlight.value = filtering.value ? (list[0] ?? null) : null
})

const highlighted = computed(() => champions.value.find(champion => champion.id === highlight.value) ?? null)
const typed = computed(() => term.value.trim())

function go(path: string) {
  router.push(path).catch((error) => {
    if (!isNavigationFailure(error)) throw error
  })
}

function openPlayers() {
  search?.open(typed.value)
}

function onFieldKeydown(event: KeyboardEvent) {
  switch (event.key) {
    case 'ArrowDown':
    case 'ArrowUp':
      if (wall.value?.move(event.key)) event.preventDefault()
      break
    case 'ArrowLeft':
    case 'ArrowRight':
      // Without a highlight the arrows still move the caret.
      if (highlight.value !== null && wall.value?.move(event.key)) event.preventDefault()
      break
    case 'Enter':
      event.preventDefault()
      if (highlight.value !== null) go(pathFor(highlight.value))
      else if (typed.value) openPlayers()
      break
    case 'Escape':
      event.preventDefault()
      if (term.value) term.value = ''
      else inputEl.value?.blur()
      break
  }
}

function focusField() {
  const input = inputEl.value
  if (!input) return
  input.focus({ preventScroll: true })
  const rect = input.getBoundingClientRect()
  if (rect.top < 0 || rect.bottom > window.innerHeight) {
    input.closest('section')?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }
}

function clearField() {
  term.value = ''
  inputEl.value?.focus()
}

// Type anywhere: a letter typed outside any field lands in this one. Digits
// stay the dock's shortcuts (no champion has one), `/` focuses the field.
function onWindowKeydown(event: KeyboardEvent) {
  if (event.defaultPrevented || event.metaKey || event.ctrlKey || event.altKey) return
  if (isEditableTarget(event.target) || dialogOpen()) return
  if (event.key === '/') {
    event.preventDefault()
    focusField()
    return
  }
  if (event.key.length === 1 && /[a-z'.&]/i.test(event.key)) focusField()
}

// What was typed into the server-rendered field before the page hydrated:
// hydration resets the field to the model, so carry it over.
const typedBeforeHydration = import.meta.client
  ? (document.getElementById('f-find-input') as HTMLInputElement | null)?.value ?? ''
  : ''

onMounted(() => {
  window.addEventListener('keydown', onWindowKeydown)
  if (typedBeforeHydration) {
    term.value = typedBeforeHydration
    inputEl.value?.focus()
  }
})
onBeforeUnmount(() => window.removeEventListener('keydown', onWindowKeydown))

const announcement = computed(() => {
  if (!filtering.value) return ''
  const n = matches.value.length
  if (n === 0) return `No champion matches. Press Enter to search players for ${typed.value}.`
  return `${n} ${n === 1 ? 'champion matches' : 'champions match'}. ${highlighted.value?.name ?? ''} highlighted, press Enter to open.`
})
</script>

<template>
  <section
    class="f-launch"
    aria-labelledby="f-launch-title"
  >
    <div
      class="f-launch__glow"
      aria-hidden="true"
    />

    <div class="f-launch__head">
      <div class="f-meta">
        <template v-if="overviewPending">
          <USkeleton class="h-3.5 w-20" />
          <USkeleton class="h-3.5 w-40" />
        </template>
        <template v-else>
          <span
            v-if="overview?.patchVersion"
            class="f-meta__patch"
          >Patch {{ overview.patchVersion }}</span>
          <span v-if="gamesAnalyzed > 0"><FxCount
            :value="gamesAnalyzed"
            :format="formatCompactCount"
            :duration="1600"
            class="f-meta__num"
          /> main games analyzed</span>
          <span v-if="trackedTruemains !== null"><span class="f-meta__num">{{ formatCompactCount(trackedTruemains) }}</span> truemains tracked</span>
        </template>
      </div>

      <h1
        id="f-launch-title"
        class="f-title"
      >
        Real builds from <span class="f-title__accent">real mains</span>.
      </h1>

      <div
        class="f-find"
        :class="{ 'is-focused': focused, 'has-term': term.length > 0 }"
      >
        <UIcon
          name="i-lucide-search"
          class="f-find__icon"
        />
        <input
          id="f-find-input"
          ref="inputEl"
          v-model="term"
          type="search"
          aria-label="Find a champion"
          class="f-find__input"
          placeholder="Which champion do you main?"
          autocomplete="off"
          autocapitalize="off"
          spellcheck="false"
          enterkeyhint="go"
          aria-controls="f-wall"
          aria-describedby="f-find-hint"
          @keydown="onFieldKeydown"
          @focus="focused = true"
          @blur="focused = false"
        >
        <button
          v-if="term"
          type="button"
          class="f-find__clear"
          aria-label="Clear the filter"
          @click="clearField"
        >
          <UIcon name="i-lucide-x" />
        </button>
        <button
          type="button"
          class="f-find__players"
          @click="openPlayers"
        >
          <span>Players</span>
          <ClientOnly>
            <span class="f-key-group">
              <UKbd
                value="meta"
                size="sm"
                class="f-key"
              />
              <UKbd
                value="K"
                size="sm"
                class="f-key"
              />
            </span>
          </ClientOnly>
        </button>
      </div>

      <p
        id="f-find-hint"
        class="f-hint"
      >
        <template v-if="!filtering">
          <span class="f-hint__lead">Just start typing</span>
          <span class="f-hint__keys"><kbd class="f-key">↑</kbd><kbd class="f-key">↓</kbd><kbd class="f-key">←</kbd><kbd class="f-key">→</kbd> move</span>
          <span class="f-hint__keys"><kbd class="f-key">↵</kbd> open</span>
          <span class="f-hint__keys f-hint__keys--wide"><kbd class="f-key">1</kbd>–<kbd class="f-key">6</kbd> pages</span>
        </template>
        <template v-else-if="highlighted">
          <span class="f-hint__keys"><kbd class="f-key">↵</kbd> open <strong>{{ highlighted.name }}</strong></span>
          <span class="f-hint__keys"><kbd class="f-key">←</kbd><kbd class="f-key">→</kbd> next</span>
          <span class="f-hint__keys"><kbd class="f-key">esc</kbd> clear</span>
        </template>
        <template v-else>
          <span class="f-hint__keys"><kbd class="f-key">↵</kbd> search players for <strong>“{{ typed }}”</strong></span>
          <span class="f-hint__keys"><kbd class="f-key">esc</kbd> clear</span>
        </template>
      </p>
      <p
        class="sr-only"
        role="status"
        aria-live="polite"
        aria-atomic="true"
      >
        {{ announcement }}
      </p>
    </div>

    <div class="f-launch__wall">
      <div class="f-legend">
        <p class="f-legend__count">
          <template v-if="!wallPending">
            <span v-if="filtering">{{ matches.length }} of {{ champions.length }} champions</span>
            <span v-else>{{ champions.length }} champions</span>
          </template>
        </p>
        <p
          v-if="hot.size > 0"
          class="f-legend__hot"
        >
          <span
            class="f-legend__ring"
            aria-hidden="true"
          />
          Hot this patch: most played and banned
        </p>
      </div>
      <DesignFWall
        ref="wall"
        v-model:highlight="highlight"
        :champions="champions"
        :matches="matches"
        :filtering="filtering"
        :hot="hot"
        :pending="wallPending"
        :field-focused="focused"
        @escape="focusField"
      >
        <template #empty>
          <div class="f-empty">
            <UIcon
              name="i-lucide-user-search"
              class="f-empty__icon"
            />
            <p class="f-empty__title">
              No champion is called “{{ typed }}”.
            </p>
            <p class="f-empty__text">
              A summoner name, maybe?
              <button
                type="button"
                class="f-link"
                @click="openPlayers"
              >
                Search players for “{{ typed }}”
              </button>
            </p>
          </div>
        </template>
      </DesignFWall>
    </div>
  </section>
</template>
