//! The browser dev scenarios that show a game (`app/app/fixtures/scenarios.json`)
//! hold the state Rust would have sent, not a hand-written one: each must be
//! exactly what the feed holds after playing the committed game tape up to the
//! scenario's game time. A scenario that disagrees with the feed is one that
//! lies about what the page will be given.

use lcu::{Played, Tape};
use live_client::{GameFeed, GameState};

fn readings() -> Vec<serde_json::Value> {
    Tape::parse(include_str!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../fixtures/ranked-game.jsonl"
    )))
    .expect("the committed game tape parses")
    .timeline()
    .into_iter()
    .filter_map(|(_, played)| match played {
        Played::Game(data) => Some(data),
        Played::Event(_) => None,
    })
    .collect()
}

fn scenario_games() -> Vec<(String, GameState)> {
    let scenarios: serde_json::Value = serde_json::from_str(include_str!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../app/app/fixtures/scenarios.json"
    )))
    .expect("the scenarios parse");
    scenarios
        .as_array()
        .expect("a list of scenarios")
        .iter()
        .filter(|scenario| !scenario["game"].is_null())
        .map(|scenario| {
            let id = scenario["id"].as_str().unwrap_or_default().to_string();
            let game = serde_json::from_value(scenario["game"].clone())
                .unwrap_or_else(|error| panic!("scenario {id}: {error}"));
            (id, game)
        })
        .collect()
}

#[test]
fn every_game_scenario_is_what_the_feed_holds_at_that_moment_of_the_tape() {
    let readings = readings();
    let scenarios = scenario_games();
    assert!(!scenarios.is_empty(), "a scenario shows a game");

    for (id, scenario) in scenarios {
        let mut feed = GameFeed::default();
        feed.follow(true);
        let mut reached = None;
        for reading in &readings {
            feed.ingest(reading);
            if reading["gameData"]["gameTime"].as_f64() == Some(scenario.game_time) {
                reached = feed.current().cloned();
                break;
            }
        }
        let held = reached.unwrap_or_else(|| {
            panic!(
                "scenario {id}: no reading of the tape is at {}",
                scenario.game_time
            )
        });
        assert_eq!(
            held,
            scenario,
            "scenario {id} is not what the feed holds; it holds:\n{}",
            serde_json::to_string(&held).unwrap()
        );
    }
}
