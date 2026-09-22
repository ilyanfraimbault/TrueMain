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

pub struct ApiClient {
    http: reqwest::Client,
    base: String,
}

impl ApiClient {
    pub fn new() -> Self {
        let base = option_env!("TRUEMAIN_API_BASE")
            .unwrap_or(DEFAULT_API_BASE)
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
        let response = self
            .http
            .post(format!("{}{path}", self.base))
            .json(body)
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

    pub async fn get<T: DeserializeOwned>(&self, path: &str) -> Result<T, String> {
        let response = self
            .http
            .get(format!("{}{path}", self.base))
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
