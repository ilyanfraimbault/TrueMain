export default defineAppConfig({
  ui: {
    // TrueMain is emerald-only on surfaces (no violet/indigo). Keep the admin
    // dashboard on the same primary so the two apps read as one product.
    colors: {
      primary: 'emerald',
      neutral: 'zinc',
    },
    fonts: false,
  },
})
