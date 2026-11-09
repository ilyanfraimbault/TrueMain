//! The overlay's placement on a screen, as the shell computes it, written to
//! `desktop/fixtures/overlay-geometry.json`. The Overlay page lays its editor
//! out with a TypeScript port of the same rules (it has to answer while a
//! panel is dragged, and in a browser with no shell); its tests read this file,
//! so the two cannot disagree. Regenerate with
//! `UPDATE_VECTORS=1 cargo test -p shell-state --test geometry_vectors`.

use serde_json::{json, Value};
use shell_state::overlay::{OverlayAnchor, OverlayPanel, OverlayPoint, OverlaySettings, Rect};
use shell_state::scoreboard::{self, HudSettings};

const SCREENS: [(f64, f64); 3] = [(1920.0, 1080.0), (2560.0, 1440.0), (1280.0, 720.0)];
const SIZES: [(f64, f64); 3] = [(232.0, 140.0), (300.0, 200.0), (1400.0, 900.0)];

fn rect((width, height): (f64, f64)) -> Rect {
    Rect {
        x: 0.0,
        y: 0.0,
        width,
        height,
    }
}

fn placements() -> Vec<Value> {
    let anchors = [
        OverlayAnchor::TopLeft,
        OverlayAnchor::TopCenter,
        OverlayAnchor::TopRight,
        OverlayAnchor::CenterLeft,
        OverlayAnchor::CenterRight,
    ];
    let customs = [None, Some((0.0, 0.0)), Some((1.0, 1.0)), Some((0.5, 0.25))];
    let mut cases = Vec::new();
    for screen in SCREENS {
        for size in SIZES {
            for anchor in anchors {
                for custom in customs {
                    let mut settings = OverlaySettings::default();
                    let panel = settings.panel_mut(OverlayPanel::NextItem);
                    panel.anchor = anchor;
                    panel.custom = custom.map(|(x, y)| OverlayPoint { x, y });
                    let (x, y) = settings.origin(OverlayPanel::NextItem, rect(screen), size);
                    cases.push(json!({
                        "anchor": anchor,
                        "custom": panel_custom(custom),
                        "screen": { "width": screen.0, "height": screen.1 },
                        "size": { "width": size.0, "height": size.1 },
                        "origin": { "x": x, "y": y },
                    }));
                }
            }
        }
    }
    cases
}

fn panel_custom(custom: Option<(f64, f64)>) -> Value {
    custom.map_or(Value::Null, |(x, y)| json!({ "x": x, "y": y }))
}

fn drops() -> Vec<Value> {
    let origins = [(0.0, 0.0), (400.0, 300.0), (5000.0, -20.0), (844.0, 470.0)];
    let mut cases = Vec::new();
    for screen in SCREENS {
        for size in SIZES {
            for origin in origins {
                let point = OverlaySettings::point_at(rect(screen), origin, size);
                cases.push(json!({
                    "screen": { "width": screen.0, "height": screen.1 },
                    "size": { "width": size.0, "height": size.1 },
                    "origin": { "x": origin.0, "y": origin.1 },
                    "custom": { "x": point.x, "y": point.y },
                }));
            }
        }
    }
    cases
}

fn scoreboards() -> Vec<Value> {
    let frames = [(1920.0, 1080.0), (2560.0, 1080.0), (1280.0, 1024.0)];
    let mut cases = Vec::new();
    for frame in frames {
        for scale in [0.0, 0.3, 1.0] {
            for nudge in [-80.0, 0.0, 25.0] {
                let hud = HudSettings {
                    global_scale: scale,
                    ..HudSettings::DEFAULTS
                };
                let rows = scoreboard::rows(rect(frame), &hud, nudge);
                let plates = scoreboard::plates_rect(rect(frame), &hud, nudge, 60.0);
                cases.push(json!({
                    "frame": { "width": frame.0, "height": frame.1 },
                    "globalScale": scale,
                    "nudge": nudge,
                    "rows": { "centreX": rows.centre_x, "top": rows.top, "pitch": rows.pitch, "width": rows.width },
                    "plates": { "x": plates.x, "y": plates.y, "width": plates.width, "height": plates.height },
                }));
            }
        }
    }
    cases
}

#[test]
fn the_checked_in_vectors_are_what_the_shell_computes() {
    let vectors = json!({
        "placements": placements(),
        "drops": drops(),
        "scoreboards": scoreboards(),
    });
    let mut text = serde_json::to_string_pretty(&vectors).expect("vectors serialise");
    text.push('\n');
    let path = concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../fixtures/overlay-geometry.json"
    );
    if std::env::var_os("UPDATE_VECTORS").is_some() {
        std::fs::write(path, &text).expect("the vectors file is writable");
        return;
    }
    let checked_in = std::fs::read_to_string(path).unwrap_or_default();
    assert!(
        checked_in.replace("\r\n", "\n") == text,
        "{path} is stale: run `UPDATE_VECTORS=1 cargo test -p shell-state --test geometry_vectors`"
    );
}
