//! The client's event stream.
//!
//! The client pushes every state change over a WebSocket, so the app does not
//! poll. Polling champ select would mean either a visible lag behind a pick or
//! a request every few hundred milliseconds for the whole session.
//!
//! The protocol is WAMP-flavoured: subscribe by sending `[5, "OnJsonApiEvent"]`,
//! then every change arrives as `[8, "OnJsonApiEvent", {uri, eventType, data}]`.

use std::sync::Arc;

use futures_util::{SinkExt, StreamExt};
use tokio::sync::mpsc;
use tokio_tungstenite::tungstenite::client::IntoClientRequest;
use tokio_tungstenite::tungstenite::Message;
use tokio_tungstenite::Connector;

use crate::credentials::Credentials;
use crate::error::{Error, Result};
use crate::tls;

/// One change the client pushed.
#[derive(Debug, Clone)]
pub struct LcuEvent {
    /// The endpoint that changed, e.g. `/lol-champ-select/v1/session`.
    pub uri: String,
    /// `Create`, `Update` or `Delete`.
    pub event_type: String,
    /// The endpoint's new body. `Null` on `Delete` — champ select ends that way.
    pub data: serde_json::Value,
}

const SUBSCRIBE_ALL: &str = r#"[5,"OnJsonApiEvent"]"#;

/// Connect, subscribe to everything, and forward events onto a channel.
///
/// Returns once the socket closes, which is the normal end of a client session:
/// the caller reconnects rather than treating it as an error.
pub async fn stream_events(credentials: Credentials, sender: mpsc::Sender<LcuEvent>) -> Result<()> {
    let mut request = format!("wss://127.0.0.1:{}/", credentials.port)
        .into_client_request()
        .map_err(|e| Error::Transport(e.to_string()))?;
    request.headers_mut().insert(
        "Authorization",
        credentials
            .authorization_header()
            .parse()
            .map_err(|_| Error::Transport("malformed authorization header".into()))?,
    );

    let connector = Connector::Rustls(Arc::new(tls::client_config()?));
    let (mut socket, _) =
        tokio_tungstenite::connect_async_tls_with_config(request, None, false, Some(connector))
            .await
            .map_err(|e| Error::Transport(e.to_string()))?;

    socket
        .send(Message::Text(SUBSCRIBE_ALL.to_string()))
        .await
        .map_err(|e| Error::Transport(e.to_string()))?;

    while let Some(message) = socket.next().await {
        let message = match message {
            Ok(message) => message,
            // A dropped socket means the client went away — the caller's
            // reconnect loop handles it, so this is not escalated.
            Err(_) => break,
        };

        let Message::Text(payload) = message else {
            continue;
        };
        let Some(event) = parse_event(&payload) else {
            continue;
        };

        // The receiver is gone: the app is shutting down.
        if sender.send(event).await.is_err() {
            break;
        }
    }

    Ok(())
}

/// Pull an event out of a `[8, "OnJsonApiEvent", {...}]` frame.
///
/// Returns `None` for anything else — the client also sends bare
/// acknowledgements, and one unparseable frame must not end the stream.
pub fn parse_event(payload: &str) -> Option<LcuEvent> {
    let frame: serde_json::Value = serde_json::from_str(payload).ok()?;
    let array = frame.as_array()?;
    if array.len() < 3 || array[0].as_i64() != Some(8) {
        return None;
    }

    let body = array[2].as_object()?;
    Some(LcuEvent {
        uri: body.get("uri")?.as_str()?.to_string(),
        event_type: body
            .get("eventType")
            .and_then(serde_json::Value::as_str)
            .unwrap_or("Update")
            .to_string(),
        data: body.get("data").cloned().unwrap_or(serde_json::Value::Null),
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_a_champ_select_update() {
        let frame = r#"[8,"OnJsonApiEvent",{"data":{"localPlayerCellId":2},"eventType":"Update","uri":"/lol-champ-select/v1/session"}]"#;
        let event = parse_event(frame).unwrap();
        assert_eq!(event.uri, "/lol-champ-select/v1/session");
        assert_eq!(event.event_type, "Update");
        assert_eq!(event.data["localPlayerCellId"], 2);
    }

    #[test]
    fn reads_the_delete_that_ends_champ_select() {
        let frame = r#"[8,"OnJsonApiEvent",{"data":null,"eventType":"Delete","uri":"/lol-champ-select/v1/session"}]"#;
        let event = parse_event(frame).unwrap();
        assert_eq!(event.event_type, "Delete");
        assert!(event.data.is_null());
    }

    #[test]
    fn ignores_the_subscription_acknowledgement() {
        assert!(parse_event(r#"[5,"OnJsonApiEvent"]"#).is_none());
    }

    #[test]
    fn one_unparseable_frame_does_not_end_the_stream() {
        // `stream_events` skips a `None` and keeps reading; this pins the
        // contract that malformed input yields `None` rather than panicking.
        assert!(parse_event("not json").is_none());
        assert!(parse_event("{}").is_none());
        assert!(parse_event("[8]").is_none());
    }
}
