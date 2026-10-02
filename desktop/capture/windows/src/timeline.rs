//! Where the game's sound lands on the video. Loopback capture delivers
//! packets only while the game plays sound and can stall, so its own count of
//! samples drifts from the video clock: each packet is placed at the video
//! time it was read at, a silence filled with silence and an overlap trimmed,
//! so the sound never runs ahead of or behind the picture by more than the
//! tolerance.

/// 100 ns units per second, Media Foundation's clock.
pub const SECOND: i64 = 10_000_000;
/// Off by less than this, a packet is written right after the last one.
const TOLERANCE: i64 = SECOND / 10;

#[derive(Debug, Default)]
pub struct AudioTimeline {
    rate: u32,
    /// Frames written so far, silence included.
    written: u64,
}

/// How to write one packet.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct Placement {
    /// Video time of the first frame written, silence included.
    pub time: i64,
    /// Silent frames to write first.
    pub pad: u64,
    /// Frames to drop from the packet's start.
    pub skip: u64,
}

impl AudioTimeline {
    pub fn new(rate: u32) -> Self {
        Self { rate, written: 0 }
    }

    fn time_of(&self, frames: u64) -> i64 {
        (frames as i128 * SECOND as i128 / i128::from(self.rate)) as i64
    }

    fn frames_in(&self, duration: i64) -> u64 {
        (i128::from(duration.max(0)) * i128::from(self.rate) / SECOND as i128) as u64
    }

    /// A packet of `frames` read at video time `now` (zero frames: no sound
    /// came, which fills the silence up to now).
    pub fn place(&mut self, frames: u64, now: i64) -> Placement {
        let end = self.time_of(self.written);
        let start = now - self.time_of(frames);
        let gap = start - end;
        let (pad, skip) = if gap > TOLERANCE {
            (self.frames_in(gap), 0)
        } else if gap < -TOLERANCE && frames > 0 {
            (0, self.frames_in(-gap).min(frames))
        } else {
            (0, 0)
        };
        self.written += pad + frames - skip;
        Placement {
            time: end,
            pad,
            skip,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const RATE: u32 = 48_000;
    const MS: i64 = SECOND / 1000;

    #[test]
    fn packets_on_time_follow_each_other() {
        let mut timeline = AudioTimeline::new(RATE);
        let first = timeline.place(480, 10 * MS);
        assert_eq!(
            first,
            Placement {
                time: 0,
                pad: 0,
                skip: 0
            }
        );
        let second = timeline.place(480, 20 * MS);
        assert_eq!(
            second,
            Placement {
                time: 10 * MS,
                pad: 0,
                skip: 0
            }
        );
    }

    #[test]
    fn a_silence_is_filled_up_to_the_packet() {
        let mut timeline = AudioTimeline::new(RATE);
        timeline.place(480, 10 * MS);
        // Nothing for two seconds, then 10 ms of sound.
        let placed = timeline.place(480, 2_010 * MS);
        assert_eq!(placed.time, 10 * MS);
        assert_eq!(placed.pad, 1_990 * 48);
        assert_eq!(timeline.place(0, 2_010 * MS).pad, 0);
    }

    #[test]
    fn an_empty_read_fills_the_silence_up_to_now() {
        let mut timeline = AudioTimeline::new(RATE);
        let placed = timeline.place(0, 500 * MS);
        assert_eq!(
            placed,
            Placement {
                time: 0,
                pad: 500 * 48,
                skip: 0
            }
        );
    }

    #[test]
    fn sound_ahead_of_the_picture_is_trimmed() {
        let mut timeline = AudioTimeline::new(RATE);
        timeline.place(0, 1_000 * MS);
        // A late burst: 500 ms of sound read at 1.2 s, of which 300 ms
        // overlaps what silence already covered.
        let placed = timeline.place(24_000, 1_200 * MS);
        assert_eq!(placed.time, 1_000 * MS);
        assert_eq!(placed.skip, 300 * 48);
    }
}
