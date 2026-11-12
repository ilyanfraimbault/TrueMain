<script setup lang="ts">
import { clamp01, documentTop, pinProgress, useScrollScene, writeVar } from './scroll-scenes'

/**
 * Design exploration, variant C — the manifesto: one paragraph, pinned, whose
 * words light up one after another as you scroll through it. It stays one real
 * `<p>` of text (every word is a span, spaces are real text nodes), so a screen
 * reader or a search engine reads a plain paragraph. Without the scroll scene
 * (no JS yet, reduced motion) every word is lit.
 */
const TEXT = 'A build is only as good as the games behind it. TrueMain reads the games of the players who truly main a champion, who pick it game after game until every matchup is muscle memory. Their runes. Their items. Their skill order. That is the build you get.'
// Lit in rose gold instead of white once reached.
const KEY_WORDS = new Set(['truly', 'main', 'runes.', 'items.', 'skill', 'order.', 'build', 'you', 'get.'])

const words = TEXT.split(' ').map((text, index, all) => ({
  text,
  // Only the closing sentence gets its key words gilded, plus "truly main".
  key: KEY_WORDS.has(text) && (index > all.length - 14 || text === 'truly' || (text === 'main' && all[index - 1] === 'truly')),
}))

const root = ref<HTMLElement | null>(null)
const cache: Record<string, number> = {}
let top = 0
let height = 0
let live = true

useScrollScene({
  measure() {
    if (!root.value) return
    live = !window.matchMedia('(prefers-reduced-motion: reduce)').matches
    top = documentTop(root.value)
    height = root.value.offsetHeight
  },
  update({ y, vh }) {
    if (!root.value) return
    if (!live) {
      writeVar(root.value, '--mp', 1, cache)
      return
    }
    const p = pinProgress(y, top, height, vh)
    // Fully lit at ~85% of the run, leaving a beat to read the last line.
    writeVar(root.value, '--mp', clamp01((p - 0.04) / 0.8), cache)
    writeVar(root.value, '--mpr', p, cache)
  },
})
</script>

<template>
  <section
    ref="root"
    class="c-manifesto"
    aria-labelledby="c-manifesto-title"
    :style="{ '--n': words.length }"
  >
    <div class="c-manifesto__stage">
      <div class="c-manifesto__head">
        <h2
          id="c-manifesto-title"
          class="c-mono"
        >
          <span class="c-manifesto__index">00</span> Manifesto
        </h2>
        <span
          class="c-manifesto__rule"
          aria-hidden="true"
        />
      </div>
      <p class="c-manifesto__text">
        <template
          v-for="(word, index) in words"
          :key="index"
        >
          <span
            class="c-word"
            :class="{ 'is-key': word.key }"
            :style="{ '--i': index }"
          >{{ word.text }}</span>{{ index < words.length - 1 ? ' ' : '' }}
        </template>
      </p>
      <div
        class="c-manifesto__meter"
        aria-hidden="true"
      >
        <span />
      </div>
    </div>
  </section>
</template>
