/**
 * The channel buttons of a known player's page are plain new-tab links, on the
 * site and here (`PlayersPlayerLinks`), but a new tab means nothing to the app's
 * webview, so the click goes to the shell, which opens Twitch, YouTube or X in
 * the player's browser (`channels.rs`) and refuses anything else. A browser
 * running `npm run dev` follows the link itself.
 */
export default defineNuxtPlugin(() => {
  if (!insideTauri()) return
  document.addEventListener('click', (event) => {
    const url = newTabLinkOf(event.target)
    if (!url) return
    event.preventDefault()
    void import('@tauri-apps/api/core').then(({ invoke }) => invoke('open_channel', { url }))
  }, true)
})
