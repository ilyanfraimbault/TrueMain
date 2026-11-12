<script setup lang="ts">
import { POSITION_OPTIONS } from '#common/utils/positions'

// The tier list's door, as a launcher's mode select: one tile per lane, each
// opening the tier list on that lane (`?position=`), and one for every lane.

const lanes = POSITION_OPTIONS.map(option => ({
  key: option.value,
  label: option.label,
  iconUrl: option.iconUrl,
  to: { path: '/champions/tierlist', query: { position: option.value } },
}))
</script>

<template>
  <section
    class="e-section"
    aria-labelledby="e-tiers-title"
  >
    <div class="e-section__head">
      <div>
        <p class="e-eyebrow">
          Tier list
        </p>
        <h2
          id="e-tiers-title"
          class="e-title"
        >
          Pick a lane.
        </h2>
        <p class="e-lede">
          Every champion's tier, win, pick and ban rate, lane by lane, from the games of the players who main them.
        </p>
      </div>
    </div>

    <ul class="e-lanes">
      <li>
        <NuxtLink
          to="/champions/tierlist"
          class="e-lane e-lane--all"
          data-e-spot
        >
          <UIcon
            name="i-lucide-layers"
            class="e-lane__icon"
          />
          <span class="e-lane__label">All lanes</span>
          <UIcon
            name="i-lucide-arrow-right"
            class="e-lane__go"
          />
        </NuxtLink>
      </li>
      <li
        v-for="lane in lanes"
        :key="lane.key"
      >
        <NuxtLink
          :to="lane.to"
          class="e-lane"
          data-e-spot
        >
          <img
            :src="lane.iconUrl"
            alt=""
            width="28"
            height="28"
            loading="lazy"
            class="e-lane__icon"
          >
          <span class="e-lane__label">{{ lane.label }}</span>
          <UIcon
            name="i-lucide-arrow-right"
            class="e-lane__go"
          />
        </NuxtLink>
      </li>
    </ul>
  </section>
</template>
