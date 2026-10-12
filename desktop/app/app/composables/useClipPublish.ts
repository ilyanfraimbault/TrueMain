import type { Clip } from '~/types/recordings'

/**
 * Publishing a clip on YouTube through YouTube's own upload page (#1815): the
 * shell opens the page in the player's browser and drags the clip's file out
 * of the app (`recording/publish.rs`); the player pastes the text the dialog
 * prepared, then gives the video's link back. No YouTube API, so no token and
 * no quota.
 */
export function useClipPublish() {
  const toast = useToast()
  const { refreshLibrary } = useRecordings()

  function failed(title: string, error: unknown) {
    toast.add({ title, description: String(error), color: 'error', icon: 'i-lucide-circle-alert' })
  }

  async function openUpload() {
    try {
      await recordingCall<null>('open_youtube_upload')
    }
    catch (error) {
      failed('YouTube could not be opened', error)
    }
  }

  async function openVideo(id: string) {
    try {
      await recordingCall<null>('clip_open_youtube', { id })
    }
    catch (error) {
      failed('The video could not be opened', error)
    }
  }

  /** From the drag handle's `dragstart`: the shell takes the drag over with the file itself. */
  async function startDrag(id: string) {
    try {
      await recordingCall<null>('clip_start_drag', { id })
    }
    catch (error) {
      failed('The clip could not be dragged', error)
    }
  }

  /** Keep the video's link (an empty one forgets it); `false` when the shell refused it. */
  /**
   * Keep the video's link once the shell has checked with YouTube that it is
   * the clip's (an empty one forgets it): `null` when kept, else why not, as
   * the shell words it — the dialog shows it under the field.
   */
  async function setYoutubeUrl(id: string, url: string): Promise<string | null> {
    try {
      await recordingCall<Clip>('clip_set_youtube_url', { id, url })
      await refreshLibrary()
      return null
    }
    catch (error) {
      const message = String(error).replace(/^Error: /, '')
      return message.charAt(0).toUpperCase() + message.slice(1)
    }
  }

  /** Delete a published clip's file, keeping the clip; `false` when the shell refused. */
  async function deleteVideo(id: string): Promise<boolean> {
    try {
      await recordingCall<Clip>('clip_delete_video', { id })
      await refreshLibrary()
      return true
    }
    catch (error) {
      failed('The video could not be removed', error)
      return false
    }
  }

  /** The page that plays a clip from YouTube (`recording/embed.rs`), or `null` when there is none. */
  const embedUrl = (id: string) => recordingCall<string>('clip_youtube_embed', { id }).catch(() => null)

  async function copy(text: string, what: string) {
    try {
      await navigator.clipboard.writeText(text)
      toast.add({ title: `${what} copied`, icon: 'i-lucide-check', duration: 1_500 })
    }
    catch (error) {
      failed(`${what} could not be copied`, error)
    }
  }

  return { openUpload, openVideo, startDrag, setYoutubeUrl, deleteVideo, embedUrl, copy }
}
