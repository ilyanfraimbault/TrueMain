//! REST access to the running client.

use serde::de::DeserializeOwned;

use crate::credentials::Credentials;
use crate::error::{Error, Result};
use crate::model::{ChampSelectSession, CurrentSummoner, GameflowPhase};
use crate::tls;

/// An authenticated connection to one running client.
///
/// Tied to the port and token it was built with, so it dies with the client
/// that issued them — which is why reconnection rebuilds it rather than
/// refreshing it.
pub struct LcuClient {
    http: reqwest::Client,
    credentials: Credentials,
}

impl LcuClient {
    pub fn new(credentials: Credentials) -> Result<Self> {
        let http = reqwest::Client::builder()
            .use_preconfigured_tls(tls::client_config()?)
            // The client is on loopback; a request that takes this long means it
            // is wedged, and the app should say "not running" rather than hang.
            .timeout(std::time::Duration::from_secs(5))
            .build()?;

        Ok(Self { http, credentials })
    }

    /// Discover a running client and connect to it.
    pub async fn connect() -> Result<Self> {
        Self::new(crate::credentials::discover().await?)
    }

    pub fn credentials(&self) -> &Credentials {
        &self.credentials
    }

    async fn get_raw(&self, path: &str) -> Result<String> {
        let response = self
            .http
            .get(format!("{}{path}", self.credentials.base_url()))
            .header(
                reqwest::header::AUTHORIZATION,
                self.credentials.authorization_header(),
            )
            .send()
            .await?;

        let status = response.status();
        if status == reqwest::StatusCode::UNAUTHORIZED {
            return Err(Error::Unauthorized);
        }
        if !status.is_success() {
            return Err(Error::UnexpectedStatus {
                status: status.as_u16(),
                path: path.to_string(),
            });
        }

        Ok(response.text().await?)
    }

    async fn get_json<T: DeserializeOwned>(&self, path: &str) -> Result<T> {
        let body = self.get_raw(path).await?;
        serde_json::from_str(&body).map_err(|e| Error::Decode(e.to_string()))
    }

    /// Where the player is. Answers a bare JSON string, not an object.
    pub async fn gameflow_phase(&self) -> Result<GameflowPhase> {
        let body = self.get_raw("/lol-gameflow/v1/gameflow-phase").await?;
        Ok(GameflowPhase::from_client_value(&body))
    }

    /// The current draft.
    ///
    /// Outside champ select the client answers 404, which is an answer and not
    /// a failure — hence `Option` rather than an error the caller has to
    /// pattern-match on a status code to interpret.
    pub async fn champ_select_session(&self) -> Result<Option<ChampSelectSession>> {
        match self.get_json("/lol-champ-select/v1/session").await {
            Ok(session) => Ok(Some(session)),
            Err(Error::UnexpectedStatus { status: 404, .. }) => Ok(None),
            Err(other) => Err(other),
        }
    }

    /// Who is logged in. This is how the app knows whose dashboard to open,
    /// with no input from the player.
    pub async fn current_summoner(&self) -> Result<CurrentSummoner> {
        self.get_json("/lol-summoner/v1/current-summoner").await
    }
}
