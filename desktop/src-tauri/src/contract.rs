//! The contract between the shell and its webview: every command the webview
//! may call and every event it may hear, with their payload types.
//!
//! The TypeScript side is generated from this list into
//! `desktop/app/app/generated/shell.ts`; `bindings_are_current` fails when that
//! file is stale. Regenerate it with
//! `UPDATE_BINDINGS=1 cargo test -p truemain-desktop contract`.

use std::marker::PhantomData;

use serde::{Deserialize, Deserializer, Serialize};
use tauri::{AppHandle, Emitter, Runtime};
use tauri_specta::{collect_commands, collect_events, Builder, ErrorHandlingMode, Event};

use crate::{
    api, champselect, game, itemsets, kits, loading, menu, overlay, record, recording, runes,
    supervisor, telemetry,
};

pub fn builder() -> Builder<tauri::Wry> {
    Builder::<tauri::Wry>::new()
        .commands(collect_commands![
            crate::current_state,
            game::current_game,
            api::commands::api_get,
            api::commands::api_post,
            crate::open_on_site,
            crate::open_channel,
            record::player_record,
            record::player_history,
            record::player_game,
            recording::recording_settings,
            recording::set_recording_settings,
            recording::recording_check_mark_key,
            recording::recording_status,
            recording::request_capture_permission,
            recording::recording_library,
            recording::recording_get,
            recording::recording_win_probability,
            recording::recording_set_kept,
            recording::recording_delete,
            recording::clip_save,
            recording::clip_update,
            recording::clip_delete,
            recording::reveal_recording_file,
            recording::open_youtube_upload,
            recording::clip_open_youtube,
            recording::clip_set_youtube_url,
            recording::clip_start_drag,
            recording::clip_delete_video,
            recording::clip_youtube_embed,
            overlay::overlay_view,
            overlay::set_overlay_settings,
            overlay::overlay_check_trigger,
            overlay::overlay_read_binds,
            overlay::overlay_preview,
            overlay::overlay_fit,
            overlay::overlay_confirmation,
            loading::loading_players,
            runes::import_runes,
            itemsets::import_item_set,
            itemsets::import_kit_item_set,
            kits::kits_get,
            kits::kits_set,
            champselect::champ_select_hover,
            champselect::champ_select_lock,
            champselect::champ_select_choices,
            champselect::champ_select_spells,
            telemetry::telemetry_page,
        ])
        .events(collect_events![
            supervisor::StateChanged,
            loading::LoadingChanged,
            game::GameSnapshot,
            game::GameChanged,
            menu::CheckForUpdates,
            overlay::OverlayViewChanged,
            overlay::ConfirmationShown,
            kits::KitsChanged,
            recording::RecordingStatusChanged,
            recording::LibraryChanged,
            recording::RecapReady,
        ])
        .constant(
            "EMPTY_STATE",
            shell_state::AppView::from(shell_state::AppState::default()),
        )
        .constant("IDLE_LOADING", loading::LoadingView::default())
        .constant("RECORDING_SCHEME", recording::files::SCHEME)
        .constant("PHASE_GROUPS", phase_groups())
        .constant("OVERLAY_PANEL_TABLE", overlay_panels())
        .constant("OVERLAY_LIMITS", overlay_limits())
        .constant("OVERLAY_GEOMETRY", overlay_geometry())
        .constant(
            "DEFAULT_OVERLAY_SETTINGS",
            shell_state::overlay::OverlaySettings::default(),
        )
        .constant(
            "DEFAULT_HUD",
            shell_state::scoreboard::HudSettings::DEFAULTS,
        )
        .constant("GOLD_STEP", live_client::GOLD_STEP)
        .constant("KEY_LABELS", key_labels())
        .error_handling(ErrorHandlingMode::Throw)
        // Ids, timestamps and sizes stay far below 2^53.
        .dangerously_cast_bigints_to_number()
        // The shell's floats are game clocks, scales and shares read from JSON
        // or computed from finite inputs: never NaN, so never `null` on the wire.
        .semantic_types(
            specta_typescript::semantic::Configuration::empty().enable_lossless_floats(),
        )
}

/// An argument the command reads itself, as leniently as the file it ends up
/// in (a value out of range falls back alone), typed `T` in the bindings.
pub struct Loose<T> {
    value: serde_json::Value,
    shape: PhantomData<T>,
}

impl<T> Loose<T> {
    pub fn json(&self) -> String {
        self.value.to_string()
    }

    pub fn into_value(self) -> serde_json::Value {
        self.value
    }
}

impl<'de, T> Deserialize<'de> for Loose<T> {
    fn deserialize<D: Deserializer<'de>>(deserializer: D) -> Result<Self, D::Error> {
        Ok(Self {
            value: serde_json::Value::deserialize(deserializer)?,
            shape: PhantomData,
        })
    }
}

impl<T: specta::Type> specta::Type for Loose<T> {
    fn definition(types: &mut specta::Types) -> specta::datatype::DataType {
        T::definition(types)
    }
}

/// A TrueMain API body the shell forwards untouched, either way: its shape is
/// the API's contract (generated from the API itself), so the bindings type it
/// `unknown` and the webview names it.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(transparent)]
pub struct ApiJson(pub serde_json::Value);

impl specta::Type for ApiJson {
    fn definition(_: &mut specta::Types) -> specta::datatype::DataType {
        specta_typescript::define("unknown").into()
    }
}

/// What a command answers when it did nothing: a reason the webview tells in
/// its own words (`R`), or a message it shows as it is.
#[derive(Debug, Serialize, specta::Type)]
#[serde(untagged)]
pub enum Refused<R> {
    Known(R),
    Other(String),
}

impl<R> From<String> for Refused<R> {
    fn from(message: String) -> Self {
        Self::Other(message)
    }
}

impl<R> From<&str> for Refused<R> {
    fn from(message: &str) -> Self {
        Self::Other(message.to_string())
    }
}

/// Each named set of client phases (`GameflowPhase::is_*`).
fn phase_groups() -> serde_json::Value {
    use lcu::GameflowPhase;
    let of = |test: fn(GameflowPhase) -> bool| -> Vec<GameflowPhase> {
        GameflowPhase::ALL
            .into_iter()
            .filter(|phase| test(*phase))
            .collect()
    };
    serde_json::json!({
        "champSelect": of(GameflowPhase::is_champ_select),
        "gameRunning": of(GameflowPhase::is_game_running),
        "gameApiUp": of(GameflowPhase::is_game_api_up),
        "postGame": of(GameflowPhase::is_post_game),
    })
}

/// Each overlay panel's name, settings field and placement.
fn overlay_panels() -> serde_json::Value {
    use shell_state::overlay::OverlayPanel;
    OverlayPanel::ALL
        .into_iter()
        .map(|panel| {
            (
                panel.slug().to_string(),
                serde_json::json!({
                    "name": panel.name(),
                    "field": panel.field(),
                    "onScoreboard": panel.on_scoreboard(),
                }),
            )
        })
        .collect::<serde_json::Map<_, _>>()
        .into()
}

/// The bounds the shell clamps the overlay's settings to.
fn overlay_limits() -> serde_json::Value {
    use shell_state::overlay::{MAX_OPACITY, MAX_SCALE, MIN_OPACITY, MIN_SCALE};
    serde_json::json!({
        "scale": { "min": MIN_SCALE, "max": MAX_SCALE },
        "opacity": { "min": MIN_OPACITY, "max": MAX_OPACITY },
        "scoreboardNudge": shell_state::scoreboard::MAX_NUDGE,
    })
}

/// The measures the overlay is placed by (`OverlaySettings::origin`,
/// `scoreboard::rows`), for the Overlay page's editor.
fn overlay_geometry() -> serde_json::Value {
    use shell_state::{overlay, scoreboard};
    serde_json::json!({
        "edgeMargin": overlay::EDGE_MARGIN,
        "topMargin": overlay::TOP_MARGIN,
        "scoreboard": {
            "reference": { "width": scoreboard::REFERENCE_WIDTH, "height": scoreboard::REFERENCE_HEIGHT },
            "rows": scoreboard::ROWS,
            "rowPitchAtZero": scoreboard::ROW_PITCH_AT_ZERO,
            "hudGrowth": scoreboard::HUD_GROWTH,
            "rowsBottomBelowMiddle": scoreboard::ROWS_BOTTOM_BELOW_MIDDLE,
            "boardWidthAtZero": scoreboard::BOARD_WIDTH_AT_ZERO,
        },
    })
}

/// The keys a chord may use, each with its name on a US keyboard.
fn key_labels() -> serde_json::Value {
    use shell_state::keys::KeyCode;
    KeyCode::ALL
        .iter()
        .map(|key| {
            let code = serde_json::to_value(key).expect("a key code serialises");
            (
                code.as_str().unwrap_or_default().to_string(),
                key.label().into(),
            )
        })
        .collect::<serde_json::Map<_, _>>()
        .into()
}

/// Send `event` to every webview, under the name its binding listens to.
pub fn emit<R: Runtime, E: Event + Serialize + Clone>(app: &AppHandle<R>, event: E) {
    let _ = app.emit(E::NAME, event);
}

/// Send `event` to the one webview labelled `label`.
pub fn emit_to<R: Runtime, E: Event + Serialize + Clone>(
    app: &AppHandle<R>,
    label: &str,
    event: E,
) {
    let _ = app.emit_to(label, E::NAME, event);
}

/// specta names a type that reads differently than it writes (a `default`
/// field is optional only when read) `X_Serialize` and `X_Deserialize`, joined
/// by an `X` union. The webview mostly reads what the shell writes, so that
/// side keeps the plain name and the other becomes `XInput`; the union goes.
#[cfg(test)]
fn name_phases(bindings: &str) -> String {
    let lines: Vec<&str> = bindings.lines().collect();
    let mut kept: Vec<&str> = Vec::with_capacity(lines.len());
    for line in lines {
        let union = line
            .strip_prefix("export type ")
            .and_then(|rest| rest.split_once(" = "))
            .is_some_and(|(name, value)| {
                value == format!("{name}_Serialize | {name}_Deserialize;")
            });
        if !union {
            kept.push(line);
            continue;
        }
        // The union carries the type's doc comment: drop it with the union,
        // the two phases carry it too.
        if kept
            .last()
            .is_some_and(|last| last.trim_end().ends_with("*/"))
        {
            while let Some(last) = kept.pop() {
                if last.trim_start().starts_with("/**") {
                    break;
                }
            }
        }
    }
    let mut text = kept.join("\n");
    text.push('\n');
    text.replace("_Serialize", "")
        .replace("_Deserialize", "Input")
}

#[cfg(test)]
mod tests {
    use std::path::PathBuf;

    use specta_typescript::Typescript;

    fn bindings_path() -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../app/app/generated/shell.ts")
    }

    #[test]
    fn bindings_are_current() {
        let fresh = std::env::temp_dir().join(format!("shell-{}.ts", std::process::id()));
        super::builder()
            .export(
                Typescript::default()
                    .header("// Generated by `desktop/src-tauri/src/contract.rs`. Do not edit."),
                &fresh,
            )
            .expect("the bindings export");
        let fresh_text =
            super::name_phases(&std::fs::read_to_string(&fresh).expect("the fresh bindings"));
        let _ = std::fs::remove_file(&fresh);

        let path = bindings_path();
        if std::env::var_os("UPDATE_BINDINGS").is_some() {
            std::fs::write(&path, &fresh_text).expect("the checked-in bindings are writable");
            return;
        }
        let checked_in = std::fs::read_to_string(&path).unwrap_or_default();
        assert!(
            checked_in.replace("\r\n", "\n") == fresh_text,
            "{} is stale: run `UPDATE_BINDINGS=1 cargo test -p truemain-desktop contract`",
            path.display()
        );
    }
}
