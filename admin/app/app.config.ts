export default defineAppConfig({
  ui: {
    // Custom palettes defined in assets/css/main.css @theme (copied from
    // `web/`, see the note there). `rosegold` is the brand accent, `ink` the
    // near-neutral surface base — the portal now shares web/'s foundations
    // (#1409), on top of the charts it already shared (#1404).
    colors: {
      primary: 'rosegold',
      neutral: 'ink',
    },
    // Card padding matches `web/`'s (#1414): the portal packs far more panels
    // per screen than the public site, and Nuxt UI's stock `p-4 sm:p-6` spent a
    // sixth of a card on its own margins. Same slots, same values, so the two
    // apps stay one design system.
    card: {
      slots: {
        header: 'p-3 sm:px-4 sm:py-3.5',
        body: 'p-3 sm:p-4',
        footer: 'p-3 sm:px-4',
      },
    },
    // Nuxt UI paints a skeleton `bg-elevated`, the same fill most cards in the
    // portal use — same collision web/ hit, and the same fix: `ink-700` reads
    // as contrast at the `animate-pulse` half-opacity a skeleton actually sits
    // at, where the next ladder step (`bg-accented`) blends back to invisible.
    // See web/app/app.config.ts for the full reasoning.
    skeleton: {
      base: 'bg-ink-700',
    },
    fonts: false,
  },
})
