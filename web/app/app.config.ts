export default defineAppConfig({
  ui: {
    colors: {
      primary: 'emerald',
      neutral: 'zinc',
    },
    // Give every UCard the app-wide glass material (translucent, blurred
    // surface — see the `glass` utility in main.css) and trim the default
    // padding a notch. Nuxt UI *appends* per-variant `root` classes rather
    // than replacing them, so the `outline` default would keep its opaque
    // `bg-default` + `ring` and bury the glass. `soft` ships only a
    // translucent `bg-elevated/50` + divider, which `glass` layers cleanly
    // on top of (blur + single border, no double ring).
    card: {
      slots: {
        root: 'glass rounded-xl overflow-hidden',
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
