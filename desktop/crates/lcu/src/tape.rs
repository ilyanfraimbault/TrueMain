//! Recording a client session, and playing it back without a client.
//!
//! Champion select is the app's whole subject and the hardest thing to reach:
//! it needs a real game, it lasts a couple of minutes, and it cannot be paused
//! to look at a panel. A tape turns one real champion select into a fixture
//! that replays as often as needed.
//!
//! A tape holds the **raw client payloads**, not the app's derived state, so a
//! replay exercises the same parsing, the same state derivation and the same
//! navigation rule the live path uses. A fixture of `AppState` would prove only
//! that the frontend renders.
//!
//! # What is recorded, and what deliberately is not
//!
//! The client's WebSocket carries *everything* it does — friends, chat, store,
//! notifications. The recorder keeps only the endpoints the supervisor acts
//! on, which is what a replay needs and nothing else. A tape still contains the
//! player's Riot ID and the PUUIDs of everyone in the lobby, so it is personal
//! data: tapes are gitignored, and the committed fixture is synthetic.
//!
//! # Why not a fake client instead
//!
//! Standing a fake LCU server on localhost would exercise more of the stack,
//! but `tls.rs` pins Riot's root and verifies the chain for real. A fake server
//! cannot produce that chain, so it would only work by opening a TLS hole in a
//! binary that ships. Replaying above the transport costs that coverage and
//! keeps the hole closed.

use std::io::Write;
use std::path::Path;
use std::time::{Duration, Instant};

use serde::{Deserialize, Serialize};

use crate::error::{Error, Result};
use crate::events::LcuEvent;

/// One line of a tape: what was read, and how long after the session started.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Entry {
    /// Milliseconds since the session was attached. The first reading is at 0.
    pub at_ms: u64,
    #[serde(flatten)]
    pub reading: Reading,
}

/// What a line holds. `kind` is the discriminant, so a tape stays readable and
/// hand-editable — building a scenario by hand is a supported use.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(tag = "kind", rename_all = "snake_case")]
pub enum Reading {
    /// `/lol-summoner/v1/current-summoner`, read once at attach.
    Summoner { data: serde_json::Value },
    /// `/lol-gameflow/v1/gameflow-phase`, read once at attach. The raw client
    /// value, quoted exactly as the client sends it.
    Phase { data: serde_json::Value },
    /// `/lol-champ-select/v1/session`, read once at attach. `null` when the
    /// session was attached outside champion select.
    Session { data: serde_json::Value },
    /// One change the client pushed afterwards.
    Event {
        uri: String,
        #[serde(default = "update")]
        event_type: String,
        data: serde_json::Value,
    },
}

fn update() -> String {
    "Update".to_string()
}

/// The readings taken at attach, before any event.
#[derive(Debug, Clone, Default)]
pub struct Initial {
    pub summoner: Option<serde_json::Value>,
    pub phase: Option<serde_json::Value>,
    pub session: Option<serde_json::Value>,
}

/// A recorded session.
#[derive(Debug, Clone, Default)]
pub struct Tape {
    pub entries: Vec<Entry>,
}

impl Tape {
    /// Parse a tape from JSON Lines.
    ///
    /// A malformed line is an error rather than a skip: a replay that silently
    /// drops a pick would be a fixture that lies, which is worse than no
    /// fixture. Blank lines are ignored so a tape can be spaced out by hand.
    pub fn parse(text: &str) -> Result<Self> {
        let mut entries = Vec::new();
        for (index, line) in text.lines().enumerate() {
            let line = line.trim();
            if line.is_empty() {
                continue;
            }
            let entry: Entry = serde_json::from_str(line)
                .map_err(|error| Error::MalformedTape(format!("line {}: {error}", index + 1)))?;
            entries.push(entry);
        }
        Ok(Self { entries })
    }

    pub fn load(path: impl AsRef<Path>) -> Result<Self> {
        Self::parse(&std::fs::read_to_string(path)?)
    }

    /// The readings taken at attach: everything before the first pushed event.
    ///
    /// Defined by position rather than by timestamp, so a hand-written tape
    /// that leaves every `at_ms` at 0 still opens on the state it describes.
    pub fn initial(&self) -> Initial {
        let mut initial = Initial::default();
        for entry in &self.entries {
            match &entry.reading {
                Reading::Summoner { data } => initial.summoner = Some(data.clone()),
                Reading::Phase { data } => initial.phase = Some(data.clone()),
                Reading::Session { data } => initial.session = Some(data.clone()),
                Reading::Event { .. } => break,
            }
        }
        initial
    }

    /// The pushed events, each with the delay to wait before delivering it.
    ///
    /// The delay is relative to the previous event, so a caller can honour the
    /// original pacing by sleeping between them — or ignore it and deliver the
    /// whole tape at once.
    pub fn events(&self) -> Vec<(Duration, LcuEvent)> {
        let mut out = Vec::new();
        let mut previous = None;
        for entry in &self.entries {
            let Reading::Event {
                uri,
                event_type,
                data,
            } = &entry.reading
            else {
                continue;
            };
            // A tape written by hand may not have ordered timestamps; a
            // negative gap is treated as no wait rather than rejected.
            let gap = entry.at_ms.saturating_sub(previous.unwrap_or(entry.at_ms));
            previous = Some(entry.at_ms);
            out.push((
                Duration::from_millis(gap),
                LcuEvent {
                    uri: uri.clone(),
                    event_type: event_type.clone(),
                    data: data.clone(),
                },
            ));
        }
        out
    }
}

/// Appends readings to a tape file as a live session runs.
///
/// Every write is best-effort: a recorder that failed must never take the app
/// down with it, so failures are logged once and then swallowed.
pub struct Recorder {
    file: std::fs::File,
    started: Instant,
    broken: bool,
}

impl Recorder {
    pub fn create(path: impl AsRef<Path>) -> Result<Self> {
        let path = path.as_ref();
        if let Some(parent) = path.parent().filter(|p| !p.as_os_str().is_empty()) {
            std::fs::create_dir_all(parent)?;
        }
        Ok(Self {
            file: std::fs::File::create(path)?,
            started: Instant::now(),
            broken: false,
        })
    }

    pub fn write(&mut self, reading: Reading) {
        if self.broken {
            return;
        }
        let entry = Entry {
            at_ms: self.started.elapsed().as_millis() as u64,
            reading,
        };
        if let Err(error) = self.append(&entry) {
            tracing::warn!(%error, "could not write to the tape; recording stops here");
            self.broken = true;
        }
    }

    fn append(&mut self, entry: &Entry) -> Result<()> {
        let mut line = serde_json::to_string(entry).map_err(|e| Error::Decode(e.to_string()))?;
        line.push('\n');
        self.file.write_all(line.as_bytes())?;
        self.file.flush()?;
        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const TAPE: &str = r#"
{"at_ms":0,"kind":"summoner","data":{"gameName":"Tester","tagLine":"EUW"}}
{"at_ms":0,"kind":"phase","data":"ChampSelect"}
{"at_ms":0,"kind":"session","data":{"myTeam":[]}}
{"at_ms":1000,"kind":"event","uri":"/lol-champ-select/v1/session","event_type":"Update","data":{"myTeam":[{"cellId":0}]}}
{"at_ms":1500,"kind":"event","uri":"/lol-gameflow/v1/gameflow-phase","event_type":"Update","data":"InProgress"}
"#;

    #[test]
    fn reads_the_attach_readings_back() {
        let initial = Tape::parse(TAPE).unwrap().initial();
        assert_eq!(initial.summoner.unwrap()["gameName"], "Tester");
        assert_eq!(initial.phase.unwrap(), "ChampSelect");
        assert!(initial.session.is_some());
    }

    #[test]
    fn keeps_the_events_in_order_with_the_gap_between_them() {
        let events = Tape::parse(TAPE).unwrap().events();
        assert_eq!(events.len(), 2);
        assert_eq!(
            events[0].0,
            Duration::ZERO,
            "the first event waits for nothing"
        );
        assert_eq!(events[0].1.uri, "/lol-champ-select/v1/session");
        assert_eq!(events[1].0, Duration::from_millis(500));
        assert_eq!(events[1].1.data, "InProgress");
    }

    #[test]
    fn a_reading_after_the_first_event_is_not_part_of_the_opening_state() {
        // Ordering carries meaning: a summoner line recorded later is a *later*
        // reading, and must not silently become the state the tape opens on.
        let tape = Tape::parse(
            r#"{"at_ms":0,"kind":"phase","data":"Lobby"}
{"at_ms":10,"kind":"event","uri":"/lol-gameflow/v1/gameflow-phase","data":"ChampSelect"}
{"at_ms":20,"kind":"summoner","data":{"gameName":"Late","tagLine":"EUW"}}"#,
        )
        .unwrap();
        assert!(tape.initial().summoner.is_none());
    }

    #[test]
    fn a_hand_written_tape_may_leave_every_timestamp_at_zero() {
        let tape = Tape::parse(
            r#"{"at_ms":0,"kind":"phase","data":"ChampSelect"}
{"at_ms":0,"kind":"event","uri":"/lol-champ-select/v1/session","data":{"myTeam":[]}}"#,
        )
        .unwrap();
        assert_eq!(tape.events()[0].0, Duration::ZERO);
        assert_eq!(tape.initial().phase.unwrap(), "ChampSelect");
    }

    #[test]
    fn event_type_defaults_so_a_tape_can_be_written_without_it() {
        let tape = Tape::parse(r#"{"at_ms":0,"kind":"event","uri":"/x","data":null}"#).unwrap();
        assert_eq!(tape.events()[0].1.event_type, "Update");
    }

    #[test]
    fn a_malformed_line_is_refused_rather_than_skipped() {
        let error = Tape::parse("{\"at_ms\":0,\"kind\":\"phase\",\"data\":\"Lobby\"}\nnot json")
            .unwrap_err();
        assert!(matches!(error, Error::MalformedTape(ref m) if m.contains("line 2")));
    }

    #[test]
    fn a_recorded_tape_reads_back_as_what_was_written() {
        let path = std::env::temp_dir().join(format!("tape-{}.jsonl", std::process::id()));
        let mut recorder = Recorder::create(&path).unwrap();
        recorder.write(Reading::Phase {
            data: serde_json::json!("ChampSelect"),
        });
        recorder.write(Reading::Event {
            uri: "/lol-champ-select/v1/session".into(),
            event_type: "Update".into(),
            data: serde_json::json!({"myTeam": []}),
        });

        let tape = Tape::load(&path).unwrap();
        assert_eq!(tape.initial().phase.unwrap(), "ChampSelect");
        assert_eq!(tape.events().len(), 1);
        std::fs::remove_file(&path).ok();
    }
}
