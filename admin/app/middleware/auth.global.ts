/**
 * Gate every route behind the operator session. Unauthenticated visitors are
 * bounced to `/login`; an already-authenticated visitor hitting `/login` is
 * sent to the dashboard root so the login page never shows once signed in.
 *
 * `useUserSession().loggedIn` is hydrated from the sealed session cookie, so
 * this runs correctly on both the server (initial render) and the client.
 */
export default defineNuxtRouteMiddleware((to) => {
  const { loggedIn } = useUserSession()

  if (to.path === '/login') {
    if (loggedIn.value) {
      return navigateTo('/')
    }
    return
  }

  if (!loggedIn.value) {
    return navigateTo('/login')
  }
})
