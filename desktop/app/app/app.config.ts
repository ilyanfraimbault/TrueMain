export default defineAppConfig({
  ui: {
    colors: {
      // The site's palette, declared in assets/css/main.css: `rosegold` is the
      // one accent, `ink` the charcoal every surface is built from.
      primary: 'rosegold',
      neutral: 'ink',
    },
    badge: {
      defaultVariants: {
        variant: 'subtle',
      },
    },
  },
})
