//! The browser dev scenarios (`app/app/fixtures/scenarios.json`) carry the
//! screen the shell would send with each state: it must be the one the
//! navigation rule derives, or the scenario shows a page the app never would.

use shell_state::{AppState, Screen};

#[test]
fn every_scenario_shows_the_screen_its_state_calls_for() {
    let scenarios: serde_json::Value = serde_json::from_str(include_str!(concat!(
        env!("CARGO_MANIFEST_DIR"),
        "/../../app/app/fixtures/scenarios.json"
    )))
    .expect("the scenarios parse");
    let scenarios = scenarios.as_array().expect("a list of scenarios");
    assert!(!scenarios.is_empty());
    for scenario in scenarios {
        let id = scenario["id"].as_str().unwrap_or_default();
        let state: AppState = serde_json::from_value(scenario["state"].clone())
            .unwrap_or_else(|error| panic!("scenario {id}: {error}"));
        let shown: Screen = serde_json::from_value(scenario["state"]["screen"].clone())
            .unwrap_or_else(|error| panic!("scenario {id} has no screen: {error}"));
        assert_eq!(shown, state.screen(), "scenario {id}");
    }
}
