//! Talking to TrueMain.
//!
//! The API has no public host of its own — the site reaches it through the web
//! app's Nitro proxy, inside the deployment's private network. The desktop app
//! has no Nitro server, so it uses that same public proxy as its entry point.
//!
//! Requests go through **Rust**, not through the webview's `fetch`. Two reasons,
//! both structural rather than stylistic: a webview request would be subject to
//! CORS against an origin the site was never configured for, and it would force
//! the app's content-security policy open to a remote host. From Rust neither
//! applies, and the CSP stays closed.

use std::time::Duration;

use serde::de::DeserializeOwned;
use serde::Serialize;

/// Where the API lives.
///
/// Overridable at build time so a developer can point a build at a local or
/// preprod stack without touching code. The default is the public site's proxy,
/// which is the only surface that exists.
const DEFAULT_API_BASE: &str = "https://truemain.lol/api";

/// The same override, read at startup instead of at compile time.
///
/// Both exist because they answer different questions: a build for preprod is
/// baked once, while someone replaying a tape against a backend they are
/// editing would otherwise have to rebuild the shell to change one URL. The
/// runtime value wins, being the more specific of the two.
const API_BASE_VAR: &str = "TRUEMAIN_API_BASE";

/// Cheap to clone: `reqwest::Client` is a handle onto one shared pool.
#[derive(Clone)]
pub struct ApiClient {
    http: reqwest::Client,
    base: String,
}

impl ApiClient {
    pub fn new() -> Self {
        let base = std::env::var(API_BASE_VAR)
            .ok()
            .filter(|value| !value.trim().is_empty())
            .or_else(|| option_env!("TRUEMAIN_API_BASE").map(str::to_string))
            .unwrap_or_else(|| DEFAULT_API_BASE.to_string())
            .trim_end_matches('/')
            .to_string();

        Self {
            http: reqwest::Client::builder()
                // Champion select is a thirty-second window. A request still in
                // flight after this is no longer worth waiting for — the panel
                // is better off saying so than freezing on a spinner.
                .timeout(Duration::from_secs(8))
                .user_agent(concat!("TrueMain-Desktop/", env!("CARGO_PKG_VERSION")))
                .build()
                .expect("failed to build the HTTP client"),
            base,
        }
    }

    pub async fn post<B: Serialize, T: DeserializeOwned>(
        &self,
        path: &str,
        body: &B,
    ) -> Result<T, String> {
        let request = self.http.post(format!("{}{path}", self.base)).json(body);
        Self::read(request, path).await
    }

    /// `post` with its own deadline, for an endpoint known to run longer than
    /// the client-wide one.
    pub async fn post_within<B: Serialize, T: DeserializeOwned>(
        &self,
        path: &str,
        body: &B,
        timeout: Duration,
    ) -> Result<T, String> {
        let request = self
            .http
            .post(format!("{}{path}", self.base))
            .json(body)
            .timeout(timeout);
        Self::read(request, path).await
    }

    pub async fn get<Q: Serialize, T: DeserializeOwned>(
        &self,
        path: &str,
        query: &Q,
    ) -> Result<T, String> {
        let request = self.http.get(format!("{}{path}", self.base)).query(query);
        Self::read(request, path).await
    }

    async fn read<T: DeserializeOwned>(
        request: reqwest::RequestBuilder,
        path: &str,
    ) -> Result<T, String> {
        let response = request
            .send()
            .await
            .map_err(|e| format!("could not reach TrueMain: {e}"))?;

        let status = response.status();
        if !status.is_success() {
            return Err(format!("TrueMain answered {status} for {path}"));
        }

        response
            .json::<T>()
            .await
            .map_err(|e| format!("could not read TrueMain's answer: {e}"))
    }
}

impl Default for ApiClient {
    fn default() -> Self {
        Self::new()
    }
}
