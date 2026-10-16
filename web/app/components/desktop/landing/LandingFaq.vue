<script setup lang="ts">
/**
 * The questions a player asks before installing a League companion (#2072),
 * answered from what the app does (`.claude/docs/features.md`, desktop) —
 * nothing here may promise more than it ships.
 */
const QUESTIONS = [
  {
    label: 'Is it free?',
    content: 'Yes. There is no account to create and nothing to sign in to: install it, open League, and it follows your games.',
  },
  {
    label: 'Is it safe to use with Riot and Vanguard?',
    content: 'It reads what Riot documents for third-party tools: the League client\'s local API and the game\'s Live Client Data API, on your own computer. It injects nothing and reads no game memory. It never plays, picks or types for you — a hover, a lock, a ban or a rune import only happens when you click it, never on its own. The overlay is a transparent window of its own above the game, which never takes your keyboard.',
  },
  {
    label: 'Which computers does it run on?',
    content: 'macOS 13.3 or later on Apple Silicon or Intel, and Windows 10 or 11, 64-bit, with the League of Legends client on the same computer.',
  },
  {
    label: 'Does the overlay work in full screen?',
    content: 'On macOS, yes, League\'s Full Screen included. On Windows, play in Borderless or Windowed: Windows draws nothing over a game in exclusive Full Screen. The panels only show while the game is in front, and a shortcut (Alt+Shift+O by default, ⌥⇧O on a Mac) hides them until the game ends.',
  },
  {
    label: 'Where are my recordings stored?',
    content: 'Recording is off until you turn it on. Games go to Movies/TrueMain on macOS and Videos\\TrueMain on Windows, each clip as its own file in a clips folder beside them. A disk budget you set deletes the oldest full games you did not keep — never a clip.',
  },
  {
    label: 'What does the app send, and where?',
    content: 'To answer a draft or a game it asks truemain.lol for the picks, builds and benchmarks that fit it. Your games and recordings stay on your computer. It also sends anonymous usage counts — how often it is opened, which pages and features — under a random id tied to neither you nor your League account; uncheck "Share Anonymous Usage Data" in the app menu (macOS) or the tray icon\'s menu (Windows) to turn them off.',
  },
  {
    label: 'Why does my computer warn me when I open it?',
    content: 'The beta is not signed by Apple or Microsoft yet, so your system asks once whether to trust it. The steps are above the requirements; after that it opens like any other app.',
  },
  {
    label: 'How does it update?',
    content: 'It checks for a new version at launch and every 15 minutes and downloads it in the background. Found at launch, it installs itself; found later, it offers "Restart now". Never in the middle of champion select or a game: an update found then waits for the phase to end.',
  },
]
</script>

<template>
  <UAccordion
    :items="QUESTIONS"
    type="multiple"
    :ui="{ item: 'border-default', trigger: 'py-4 text-base font-medium text-highlighted', body: 'pb-5 text-sm leading-relaxed text-muted text-pretty max-w-3xl' }"
  />
</template>
