// Moments the apps may count, raised as Nuxt hooks so the shared layer never
// depends on an analytics tracker: the site's Umami plugin listens and sends an
// anonymous event, the desktop app does not listen at all (#2357).
declare module '#app' {
  interface RuntimeNuxtHooks {
    /** A Riot ID (`Name#TAG`) typed in the search found no tracked truemain. */
    'truemain:search-miss': () => void
    /** A truemain profile page answered "player not found". */
    'truemain:profile-not-found': () => void
  }
}

export {}
