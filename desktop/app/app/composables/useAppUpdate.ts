/**
 * The beta updates itself: once per launch the shell asks the site's update
 * feed (`plugins.updater.endpoints` in tauri.conf.json) whether a newer signed
 * build exists, and a toast offers it. Nothing installs without the player's
 * click — a restart in the middle of champion select would cost them the
 * draft — and a failed check stays silent: the app works on the version it has.
 */
export function useAppUpdate() {
  const toast = useToast()
  const installing = ref(false)

  async function check() {
    // `npm run dev` has no shell to update, and a dev build is not a release.
    if (!insideTauri() || import.meta.dev) return
    try {
      const { check: checkForUpdate } = await import('@tauri-apps/plugin-updater')
      const update = await checkForUpdate()
      if (!update) return
      toast.add({
        id: 'app-update',
        title: `TrueMain ${update.version} is available`,
        description: 'Restart to update. Your settings and favorites stay.',
        icon: 'i-lucide-download',
        duration: 0,
        actions: [{
          label: 'Update and restart',
          color: 'primary',
          onClick: async () => {
            if (installing.value) return
            installing.value = true
            try {
              await update.downloadAndInstall()
              const { relaunch } = await import('@tauri-apps/plugin-process')
              await relaunch()
            }
            catch {
              installing.value = false
              toast.add({ title: 'The update could not be installed', description: 'It will be offered again at the next launch.', color: 'error', icon: 'i-lucide-circle-alert' })
            }
          },
        }],
      })
    }
    catch {
      // Offline, the feed down, no release yet: the app runs as it is.
    }
  }

  return { check }
}
