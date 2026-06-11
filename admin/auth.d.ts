// Augment nuxt-auth-utils' session types. The module ships an empty `User`
// interface by design and expects the app to declare its own shape. Our
// single-operator login only ever stores the display name.
declare module '#auth-utils' {
  interface User {
    name: string
  }
}

export {}
