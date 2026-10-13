import { formatRate, PLAYBACK_RATES } from '~/utils/recording-timeline'

/**
 * The recap player's playback speed (0.25× to 4×). It holds for the rest of the
 * session, across recordings and clips.
 */
export function usePlaybackRate(video: Ref<HTMLVideoElement | null>, onSet: () => void) {
  const rate = useState('recap-playback-rate', () => 1)

  /** A new source resets an element's speed to its default, so both are set, and set again once it has loaded. */
  function applyRate() {
    if (!video.value) return
    video.value.defaultPlaybackRate = rate.value
    video.value.playbackRate = rate.value
  }

  function setRate(value: number) {
    rate.value = value
    applyRate()
    onSet()
  }

  const rateItems = computed(() => [...PLAYBACK_RATES].reverse().map(value => ({
    label: value === 1 ? 'Normal' : formatRate(value),
    type: 'checkbox' as const,
    checked: rate.value === value,
    onUpdateChecked: () => setRate(value),
  })))

  return { rate, applyRate, setRate, rateItems }
}
