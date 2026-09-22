//! REST access to the running client.

use serde::de::DeserializeOwned;

use crate::credentials::Credentials;
use crate::error::{Error, Result};
use crate::model::{ChampSelectSession, CurrentSummoner, GameflowPhase};
use crate::runes::{plan_import, RuneImportPlan, RunePage, RunePageDraft};
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

/// The write path: pushing a rune page into the client.
impl LcuClient {
    /// How many pages the client lets a player keep. Read from the client when
    /// it answers, since Riot has raised this before and a hard-coded ceiling
    /// would start refusing imports on a list that still has room.
    async fn owned_page_limit(&self) -> usize {
        #[derive(serde::Deserialize)]
        #[serde(rename_all = "camelCase")]
        struct Inventory {
            owned_page_count: Option<usize>,
        }

        self.get_json::<Inventory>("/lol-perks/v1/inventory")
            .await
            .ok()
            .and_then(|inventory| inventory.owned_page_count)
            .unwrap_or(2)
    }

    pub async fn rune_pages(&self) -> Result<Vec<RunePage>> {
        self.get_json("/lol-perks/v1/pages").await
    }

    /// Push a rune page and select it.
    ///
    /// Call this from an explicit user action only. It is the single place this
    /// app writes to the client, and the distance between doing it on a click
    /// and doing it on a pick is the distance between a tool and a policy
    /// violation.
    ///
    /// Reuses the page this app owns when one exists, so a player does not end
    /// a session with one TrueMain page per game. When the list is full and
    /// none of it is ours, this fails with [`Error::NoRunePageSlot`] rather
    /// than freeing a slot by deleting a page the player made.
    pub async fn import_rune_page(&self, draft: &RunePageDraft) -> Result<i64> {
        let pages = self.rune_pages().await?;
        match plan_import(&pages, self.owned_page_limit().await) {
            RuneImportPlan::NoSlotAvailable => return Err(Error::NoRunePageSlot),
            RuneImportPlan::ReplaceOwn { page_id } => {
                self.delete(&format!("/lol-perks/v1/pages/{page_id}"))
                    .await?;
            }
            RuneImportPlan::Create => {}
        }

        let created: RunePage = self.post_json("/lol-perks/v1/pages", draft).await?;
        self.put_json("/lol-perks/v1/currentpage", &created.id)
            .await?;
        Ok(created.id)
    }

    async fn send<B: serde::Serialize>(
        &self,
        method: reqwest::Method,
        path: &str,
        body: Option<&B>,
    ) -> Result<String> {
        let mut request = self
            .http
            .request(method, format!("{}{path}", self.credentials.base_url()))
            .header(
                reqwest::header::AUTHORIZATION,
                self.credentials.authorization_header(),
            );
        if let Some(body) = body {
            request = request.json(body);
        }

        let response = request.send().await?;
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

    async fn post_json<B: serde::Serialize, T: DeserializeOwned>(
        &self,
        path: &str,
        body: &B,
    ) -> Result<T> {
        let text = self.send(reqwest::Method::POST, path, Some(body)).await?;
        serde_json::from_str(&text).map_err(|e| Error::Decode(e.to_string()))
    }

    async fn put_json<B: serde::Serialize>(&self, path: &str, body: &B) -> Result<()> {
        self.send(reqwest::Method::PUT, path, Some(body)).await?;
        Ok(())
    }

    async fn delete(&self, path: &str) -> Result<()> {
        self.send::<()>(reqwest::Method::DELETE, path, None).await?;
        Ok(())
    }
}
