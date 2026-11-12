<script setup lang="ts">
import type { RouteLocationRaw } from 'vue-router'
import { championDisplayName } from '~~/shared/utils/champion-name'
import { getProfileIconUrl } from '~~/shared/utils/ddragon'
import { truemainProfilePath } from '~~/shared/utils/truemain-path'
import { POSITION_BY_VALUE } from '#shared/utils/positions'
import { navGroups, shortPatch } from './nav'

// Variant D ("Cockpit"): the sidebar's content — brand, search, the grouped
// navigation with its gliding light, the reader's pinned favorites and the live
// patch. Rendered twice by the shell: in the desktop sidebar (`collapsed`
// narrows it to an icon rail, toggled from the context bar) and in the mobile
// drawer (`drawer`: no search, the top bar has it).
const props = withDefaults(defineProps<{
  /** Icon rail: labels hidden, tooltips on. Desktop sidebar only. */
  collapsed?: boolean
  drawer?: boolean
}>(), {
  collapsed: false,
  drawer: false,
})

const emit = defineEmits<{ close: [] }>()

const route = useRoute()
const groups = computed(() => navGroups(route.path))
const rail = computed(() => props.collapsed && !props.drawer)

// ─── Patch ────────────────────────────────────────────────────────────────
// Client-only list (`server: false`): the chip skeletons until it lands.
const { data: versions } = useDDragonVersions()
const ddragonVersion = computed(() => versions.value?.[0] ?? null)
const patch = computed(() => shortPatch(ddragonVersion.value))

// ─── Pinned favorites ─────────────────────────────────────────────────────
// Read from `localStorage` after mount (the composables' hydration contract):
// empty on the server and through hydration, filled as an ordinary update.
const { favorites, hydrated } = useFavoriteTruemains()
const { champions: starred } = useFavoriteChampions()
const { data: staticList } = useChampionStaticList()
const championsById = useChampionsById(staticList)
const { pathFor } = useChampionSlugs()
const canonicalIcon = useCanonicalIcon()

const MAX_PINS = 6

interface Pin {
  key: string
  label: string
  detail: string | null
  to: RouteLocationRaw
  icon: string | undefined
  round: boolean
}

const pins = computed<Pin[]>(() => {
  const champions = starred.value.map(f => ({
    addedAt: f.addedAt,
    pin: {
      key: `c:${f.championId}:${f.position ?? ''}`,
      label: championDisplayName(f.championId, championsById.value.get(f.championId)),
      detail: f.position ? POSITION_BY_VALUE.get(f.position)?.shortLabel ?? null : null,
      to: { path: pathFor(f.championId), query: f.position ? { position: f.position } : {} },
      icon: canonicalIcon(championsById.value.get(f.championId)?.iconUrl, 48),
      round: false,
    },
  }))
  const players = favorites.value.map(f => ({
    addedAt: f.addedAt,
    pin: {
      key: `p:${f.nameTag}`,
      label: f.gameName,
      detail: f.tagLine ? `#${f.tagLine}` : null,
      to: truemainProfilePath(f.nameTag),
      icon: f.profileIconId ? canonicalIcon(getProfileIconUrl(f.profileIconId, ddragonVersion.value), 48) : undefined,
      round: true,
    },
  }))
  return [...champions, ...players]
    .sort((a, b) => b.addedAt - a.addedAt)
    .slice(0, MAX_PINS)
    .map(entry => entry.pin)
})

// ─── The gliding light ────────────────────────────────────────────────────
// One highlight under the nav: it rests on the current page and follows the
// pointer (or the keyboard focus) across the others, then returns.
const navEl = ref<HTMLElement | null>(null)
const listEl = ref<HTMLElement | null>(null)
const hovered = ref<string | null>(null)

const activeKey = computed(() => {
  for (const group of groups.value) {
    const item = group.items.find(i => i.active)
    if (item) return item.to
  }
  return null
})
const targetKey = computed(() => hovered.value ?? activeKey.value)
const light = ref({ y: 0, h: 0, shown: false, placed: false })

function measure() {
  const nav = navEl.value
  const key = targetKey.value
  const el = nav && key ? nav.querySelector<HTMLElement>(`[data-d-key="${CSS.escape(key)}"]`) : null
  if (!el) {
    light.value = { ...light.value, shown: false }
    return
  }
  light.value = { ...light.value, y: el.offsetTop, h: el.offsetHeight, shown: true }
}

watch([targetKey, () => route.path], () => nextTick(measure))

const lightStyle = computed(() => ({
  transform: `translate3d(0, ${light.value.y}px, 0)`,
  height: `${light.value.h}px`,
}))

function onLeave(event: FocusEvent) {
  // Focus moving to another link of the nav keeps the light with it.
  if (!navEl.value?.contains(event.relatedTarget as Node | null)) hovered.value = null
}

let resizeObserver: ResizeObserver | null = null
const lightEl = ref<HTMLElement | null>(null)
onMounted(async () => {
  measure()
  // Placed under the current page first (rendered and styled without a
  // transition), then allowed to glide.
  await nextTick()
  // Reading a layout value flushes the pending style.
  void lightEl.value?.offsetWidth
  light.value = { ...light.value, placed: true }
  if (listEl.value) {
    resizeObserver = new ResizeObserver(() => measure())
    resizeObserver.observe(listEl.value)
  }
})
onBeforeUnmount(() => resizeObserver?.disconnect())

const tooltip = { side: 'right' as const, sideOffset: 12 }
</script>

<template>
  <div
    class="d-sb"
    :class="{ 'd-sb--drawer': drawer }"
  >
    <div class="d-sb__head">
      <NuxtLink
        to="/"
        class="d-sb__brand"
        aria-label="TrueMain overview"
      >
        <DesignDLogo class="text-[17px]" />
      </NuxtLink>

      <button
        v-if="drawer"
        type="button"
        class="d-sb__close"
        aria-label="Close menu"
        @click="emit('close')"
      >
        <UIcon
          name="i-lucide-x"
          class="size-[18px]"
        />
      </button>
    </div>

    <div
      v-if="!drawer"
      class="d-sb__search"
    >
      <UTooltip
        text="Search"
        :kbds="['meta', 'K']"
        :disabled="!rail"
        :content="tooltip"
      >
        <AppSearch
          variant="field"
          size="sm"
          shortcut
          placeholder="Search…"
        />
      </UTooltip>
    </div>

    <nav
      ref="navEl"
      class="d-sb__nav"
      aria-label="Main"
      @pointerleave="hovered = null"
      @focusout="onLeave"
    >
      <span
        ref="lightEl"
        aria-hidden="true"
        class="d-sb__light"
        :class="{
          'is-shown': light.shown,
          'is-placed': light.placed,
          'is-resting': hovered === null || hovered === activeKey,
        }"
        :style="lightStyle"
      />

      <div
        ref="listEl"
        class="d-sb__list"
      >
        <div
          v-for="(group, groupIndex) in groups"
          :key="group.label ?? groupIndex"
          class="d-sb__group"
        >
          <p
            v-if="group.label"
            class="d-sb__heading"
          >
            <span>{{ group.label }}</span>
          </p>
          <ul>
            <li
              v-for="item in group.items"
              :key="item.to"
            >
              <UTooltip
                :text="item.label"
                :disabled="!rail"
                :content="tooltip"
              >
                <NuxtLink
                  :to="item.to"
                  class="d-sb__link"
                  :class="{ 'is-active': item.active }"
                  :aria-current="item.active ? 'page' : undefined"
                  :data-d-key="item.to"
                  @pointerenter="hovered = item.to"
                  @focus="hovered = item.to"
                >
                  <UIcon
                    :name="item.icon"
                    class="d-sb__icon"
                  />
                  <span class="d-sb__label">{{ item.label }}</span>
                  <span
                    v-if="item.badge"
                    class="d-sb__badge"
                  >{{ item.badge }}</span>
                </NuxtLink>
              </UTooltip>
            </li>
          </ul>

          <!-- The reader's pins, under the players group: starred champions
               and followed truemains, newest first. -->
          <template v-if="group.label === 'Players'">
            <ul
              v-if="pins.length > 0"
              class="d-sb__pins"
              aria-label="Pinned favorites"
            >
              <li
                v-for="pin in pins"
                :key="pin.key"
              >
                <UTooltip
                  :text="pin.detail ? `${pin.label} ${pin.detail}` : pin.label"
                  :disabled="!rail"
                  :content="tooltip"
                >
                  <NuxtLink
                    :to="pin.to"
                    class="d-sb__link d-sb__pin"
                    :data-d-key="pin.key"
                    @pointerenter="hovered = pin.key"
                    @focus="hovered = pin.key"
                  >
                    <span
                      class="d-sb__pin-icon"
                      :class="{ 'is-round': pin.round }"
                    >
                      <img
                        v-if="pin.icon"
                        :src="pin.icon"
                        alt=""
                        width="20"
                        height="20"
                        loading="lazy"
                        class="img-skeleton"
                      >
                      <UIcon
                        v-else
                        name="i-lucide-user-round"
                        class="size-3"
                      />
                    </span>
                    <span class="d-sb__label">{{ pin.label }}</span>
                    <span
                      v-if="pin.detail"
                      class="d-sb__pin-detail"
                    >{{ pin.detail }}</span>
                  </NuxtLink>
                </UTooltip>
              </li>
            </ul>
            <p
              v-else-if="hydrated"
              class="d-sb__hint"
            >
              Star a champion or follow a player to pin it here.
            </p>
          </template>
        </div>
      </div>
    </nav>

    <div class="d-sb__foot">
      <UTooltip
        text="Latest League of Legends patch"
        :content="tooltip"
      >
        <div class="d-sb__patch">
          <span
            class="d-sb__patch-dot"
            aria-hidden="true"
          />
          <span class="d-sb__patch-label">Patch</span>
          <span
            v-if="patch"
            class="d-sb__patch-value"
          >{{ patch }}</span>
          <USkeleton
            v-else
            class="d-sb__patch-skeleton h-3 w-9"
          />
        </div>
      </UTooltip>
    </div>
  </div>
</template>
