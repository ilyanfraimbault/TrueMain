/**
 * The pages shared with the site (#1732) link to routes only the site has — a
 * player's profile, their champion page. The app has no such page, so a
 * navigation that matches none of its routes opens it on truemain.lol instead,
 * and the app stays where it was.
 */
export default defineNuxtPlugin(() => {
  const router = useRouter()
  router.beforeEach((to) => {
    if (to.matched.length > 0) return
    void openOnSite(to.fullPath)
    return false
  })
})
