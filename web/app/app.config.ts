export default defineAppConfig({
  ui: {
    colors: {
      // Custom palettes defined in assets/css/main.css @theme. `rosegold` is
      // the brand accent (warm rose with a gold read); `mauve` is a warm
      // near-neutral charcoal replacing the old cold zinc.
      primary: 'rosegold',
      neutral: 'mauve',
    },
    // Give every UCard the app-wide glass material (translucent, blurred
    // surface — see the `glass` utility in main.css) and trim the default
    // padding a notch. Nuxt UI *appends* per-variant `root` classes rather
    // than replacing them, so the `outline` default would keep its opaque
    // `bg-default` + `ring` and bury the glass. `soft` ships only a
    // translucent `bg-elevated/50` + divider, which `glass` layers cleanly
    // on top of (blur + single border, no double ring).
    card: {
      // No `overflow-hidden` on the root: combined with the `glass` utility's
      // `backdrop-filter` it triggers a WebKit bug where the blur ignores the
      // border-radius and bleeds past the rounded corners on Safari/iOS. The
      // body/header padding keeps content off the corners anyway.
      slots: {
        root: 'glass rounded-2xl',
        header: 'p-3 sm:px-4 sm:py-3.5',
        body: 'p-3 sm:p-4',
        footer: 'p-3 sm:px-4',
      },
      defaultVariants: {
        variant: 'soft',
      },
    },
  },
})
