<script setup lang="ts">
import { championDisplayName } from '~~/shared/utils/champion-name'
import { truemainSlugLabel } from '~~/shared/utils/truemain-path'
import { routeTrail, shortPatch } from './nav'

// Variant D ("Cockpit"): the slim bar over the content column, desktop only.
// The sidebar's toggle, where you are (the section's icon and a trail read from
// the route), the live patch and one quick action. Translucent, so the page
// slides under it; its hairline firms up once the page has scrolled.
defineProps<{ collapsed: boolean }>()
const emit = defineEmits<{ toggle: [] }>()

const route = useRoute()
const { resolveParam } = useChampionSlugs()
const { data: staticList } = useChampionStaticList()
const championsById = useChampionsById(staticList)

function nameOf(segment: string, parent: string): string | null {
  if (parent === 'champions') {
    const { championId } = resolveParam(segment)
    return championId ? championDisplayName(championId, championsById.value.get(championId)) : null
  }
  if (parent === 'truemains') return truemainSlugLabel(segment)
  return null
}

const trail = computed(() => routeTrail(route.path, nameOf))

const { data: versions } = useDDragonVersions()
const patch = computed(() => shortPatch(versions.value?.[0]))

// Scrolled state from a sentinel at the top of the column: no scroll handler.
const sentinel = ref<HTMLElement | null>(null)
const scrolled = ref(false)
let observer: IntersectionObserver | null = null
onMounted(() => {
  if (!sentinel.value) return
  observer = new IntersectionObserver(([entry]) => {
    scrolled.value = !entry?.isIntersecting
  })
  observer.observe(sentinel.value)
})
onBeforeUnmount(() => observer?.disconnect())
</script>

<template>
  <span
    ref="sentinel"
    aria-hidden="true"
    class="d-ctx__sentinel"
  />
  <div
    class="d-ctx"
    :class="{ 'is-scrolled': scrolled }"
  >
    <UTooltip
      :text="collapsed ? 'Expand sidebar' : 'Collapse sidebar'"
      :kbds="['[']"
      :content="{ side: 'bottom', sideOffset: 8 }"
    >
      <button
        type="button"
        class="d-ctx__toggle"
        :aria-label="collapsed ? 'Expand sidebar' : 'Collapse sidebar'"
        :aria-expanded="!collapsed"
        aria-controls="d-sidebar"
        @click="emit('toggle')"
      >
        <UIcon
          :name="collapsed ? 'i-lucide-panel-left-open' : 'i-lucide-panel-left-close'"
          class="size-[17px]"
        />
      </button>
    </UTooltip>
    <span
      class="d-ctx__divider"
      aria-hidden="true"
    />

    <nav
      aria-label="Breadcrumb"
      class="d-ctx__trail"
    >
      <UIcon
        :name="trail.icon"
        class="d-ctx__icon"
      />
      <ol>
        <li
          v-for="(crumb, index) in trail.crumbs"
          :key="index"
        >
          <UIcon
            v-if="index > 0"
            name="i-lucide-chevron-right"
            class="d-ctx__sep"
          />
          <NuxtLink
            v-if="crumb.to"
            :to="crumb.to"
            class="d-ctx__crumb"
          >
            {{ crumb.label }}
          </NuxtLink>
          <span
            v-else
            class="d-ctx__crumb is-current"
            aria-current="page"
          >{{ crumb.label }}</span>
        </li>
      </ol>
    </nav>

    <div class="d-ctx__actions">
      <span
        v-if="patch"
        class="d-chip"
      >
        <span
          class="d-chip__dot"
          aria-hidden="true"
        />
        Patch {{ patch }}
      </span>
      <NuxtLink
        v-if="route.path !== '/download'"
        to="/download"
        class="d-ctx__action"
      >
        <UIcon
          name="i-lucide-monitor-down"
          class="size-3.5"
        />
        Get the app
      </NuxtLink>
    </div>
  </div>
</template>
