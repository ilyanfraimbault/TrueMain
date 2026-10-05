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

use std::collections::HashMap;
use std::future::Future;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

use serde::de::DeserializeOwned;
use serde::Serialize;

/// Where the API lives: the proxy of the site this build belongs to
/// (`site.rs`), which is the only surface that exists.
///
/// Overridable at startup with `TRUEMAIN_API_BASE`, for someone replaying a
/// tape against a backend they are editing — a local API answers without the
/// proxy's `/api` prefix, so it is a different base, not a different site.
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
            .unwrap_or_else(|| format!("{}/api", crate::site::base()))
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

    /// `post_within` with a query string, for a paged POST read.
    pub async fn post_query_within<Q: Serialize, B: Serialize, T: DeserializeOwned>(
        &self,
        path: &str,
        query: &Q,
        body: &B,
        timeout: Duration,
    ) -> Result<T, String> {
        let request = self
            .http
            .post(format!("{}{path}", self.base))
            .query(query)
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

    /// `get` with its own deadline, for a read known to run longer than the
    /// client-wide one.
    pub async fn get_within<Q: Serialize, T: DeserializeOwned>(
        &self,
        path: &str,
        query: &Q,
        timeout: Duration,
    ) -> Result<T, String> {
        let request = self
            .http
            .get(format!("{}{path}", self.base))
            .query(query)
            .timeout(timeout);
        Self::read(request, path).await
    }

    /// A POST whose answer carries nothing to read: a report, not a read —
    /// the usage counts (`telemetry.rs`).
    pub async fn send<B: Serialize>(&self, path: &str, body: &B) -> Result<(), String> {
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
        Ok(())
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

/// How long an answer stays good for a read asked again, identical.
const REUSE_FOR: Duration = Duration::from_secs(2);

type Answer = Result<serde_json::Value, String>;
/// When the read was first asked, and its answer once it has one.
type Read = (Instant, Arc<tokio::sync::OnceCell<Answer>>);

/// Identical reads asked at the same moment, answered by one request
/// (#1916). The game page and the overlay's panel both ask for the next item
/// on the same reading of the game, each from its own webview: the shell
/// sends one request and hands both the answer. A failure is never kept, so
/// the next ask tries again.
#[derive(Default)]
pub struct SharedReads {
    reads: Mutex<HashMap<String, Read>>,
}

impl SharedReads {
    /// `fetch`'s answer, or the one an identical read (`key`) got or is
    /// getting.
    pub async fn read<F, Fut>(&self, key: String, fetch: F) -> Answer
    where
        F: FnOnce() -> Fut,
        Fut: Future<Output = Answer>,
    {
        let cell = {
            let mut reads = self.reads.lock().expect("reads mutex poisoned");
            let now = Instant::now();
            reads.retain(|_, (at, _)| now.duration_since(*at) < REUSE_FOR);
            reads
                .entry(key.clone())
                .or_insert_with(|| (now, Arc::default()))
                .1
                .clone()
        };
        let answer = cell.get_or_init(fetch).await.clone();
        if answer.is_err() {
            let mut reads = self.reads.lock().expect("reads mutex poisoned");
            if reads
                .get(&key)
                .is_some_and(|(_, kept)| Arc::ptr_eq(kept, &cell))
            {
                reads.remove(&key);
            }
        }
        answer
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicU32, Ordering};

    async fn counted(sent: &AtomicU32, answer: Answer) -> Answer {
        sent.fetch_add(1, Ordering::SeqCst);
        tokio::time::sleep(Duration::from_millis(20)).await;
        answer
    }

    #[tokio::test]
    async fn identical_reads_at_once_send_one_request() {
        let reads = SharedReads::default();
        let sent = AtomicU32::new(0);
        let ok = || counted(&sent, Ok(serde_json::json!({ "itemId": 3157 })));
        let (a, b) = tokio::join!(reads.read("next".into(), ok), reads.read("next".into(), ok));
        assert_eq!(a, b);
        assert_eq!(sent.load(Ordering::SeqCst), 1);
        // Another question is its own request.
        let _ = reads.read("other".into(), ok).await;
        assert_eq!(sent.load(Ordering::SeqCst), 2);
    }

    #[tokio::test]
    async fn a_failure_is_asked_again() {
        let reads = SharedReads::default();
        let sent = AtomicU32::new(0);
        let failed = reads
            .read("next".into(), || counted(&sent, Err("offline".into())))
            .await;
        assert!(failed.is_err());
        let answered = reads
            .read("next".into(), || counted(&sent, Ok(serde_json::json!(1))))
            .await;
        assert!(answered.is_ok());
        assert_eq!(sent.load(Ordering::SeqCst), 2);
    }
}
