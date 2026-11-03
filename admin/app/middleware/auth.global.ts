import { loginLocation, safeRedirectPath } from '~~/shared/utils/login-redirect'

/**
 * Gate every route behind the operator session. Unauthenticated visitors are
 * bounced to `/login?redirect=<the page they asked for>` (#2289); an
 * already-authenticated visitor hitting `/login` is sent on to that redirect
 * (sanitised, `/` by default) so the login page never shows once signed in.
 *
 * `useUserSession().loggedIn` is hydrated from the sealed session cookie, so
 * this runs correctly on both the server (initial render) and the client.
 */
export default defineNuxtRouteMiddleware((to) => {
  const { loggedIn } = useUserSession()

  if (to.path === '/login') {
    if (loggedIn.value) {
      return navigateTo(safeRedirectPath(to.query.redirect))
    }
    return
  }

  if (!loggedIn.value) {
    return navigateTo(loginLocation(to.fullPath))
  }
})
