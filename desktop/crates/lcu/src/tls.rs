//! TLS for a server that is ours but whose certificate is not for us.
//!
//! The client serves a certificate issued by Riot's own root for an internal
//! hostname, while we connect to `127.0.0.1`. Two bad options are commonly
//! taken: trust everything (`danger_accept_invalid_certs`), which would also
//! trust anything else that answered on that port, or skip TLS entirely.
//!
//! We take the third: pin Riot's root, verify the chain for real, and tolerate
//! **only** the name mismatch — which is the one thing that cannot match by
//! construction, since the address is a loopback literal. Anything else about
//! the certificate still has to check out.

use std::sync::Arc;

use rustls::client::danger::{HandshakeSignatureValid, ServerCertVerified, ServerCertVerifier};
use rustls::client::WebPkiServerVerifier;
use rustls::pki_types::{CertificateDer, ServerName, UnixTime};
use rustls::{
    CertificateError, ClientConfig, DigitallySignedStruct, RootCertStore, SignatureScheme,
};

use crate::error::{Error, Result};

/// Riot's root, vendored so the app never fetches it at runtime.
const RIOT_ROOT_PEM: &[u8] = include_bytes!("../certs/riotgames.pem");

#[derive(Debug)]
struct LoopbackNameVerifier {
    inner: Arc<WebPkiServerVerifier>,
}

impl ServerCertVerifier for LoopbackNameVerifier {
    fn verify_server_cert(
        &self,
        end_entity: &CertificateDer<'_>,
        intermediates: &[CertificateDer<'_>],
        server_name: &ServerName<'_>,
        ocsp_response: &[u8],
        now: UnixTime,
    ) -> std::result::Result<ServerCertVerified, rustls::Error> {
        match self.inner.verify_server_cert(
            end_entity,
            intermediates,
            server_name,
            ocsp_response,
            now,
        ) {
            Ok(verified) => Ok(verified),
            // The chain is good and only the hostname disagrees — expected, and
            // the only deviation this verifier grants.
            Err(rustls::Error::InvalidCertificate(CertificateError::NotValidForName)) => {
                Ok(ServerCertVerified::assertion())
            }
            Err(other) => Err(other),
        }
    }

    fn verify_tls12_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> std::result::Result<HandshakeSignatureValid, rustls::Error> {
        self.inner.verify_tls12_signature(message, cert, dss)
    }

    fn verify_tls13_signature(
        &self,
        message: &[u8],
        cert: &CertificateDer<'_>,
        dss: &DigitallySignedStruct,
    ) -> std::result::Result<HandshakeSignatureValid, rustls::Error> {
        self.inner.verify_tls13_signature(message, cert, dss)
    }

    fn supported_verify_schemes(&self) -> Vec<SignatureScheme> {
        self.inner.supported_verify_schemes()
    }
}

/// A TLS configuration that trusts Riot's root and nothing else.
pub fn client_config() -> Result<ClientConfig> {
    let provider = Arc::new(rustls::crypto::ring::default_provider());

    let mut roots = RootCertStore::empty();
    let mut pem = RIOT_ROOT_PEM;
    for certificate in rustls_pemfile::certs(&mut pem) {
        let certificate = certificate.map_err(|e| Error::Certificate(e.to_string()))?;
        roots
            .add(certificate)
            .map_err(|e| Error::Certificate(e.to_string()))?;
    }
    if roots.is_empty() {
        return Err(Error::Certificate(
            "the vendored PEM contained no certificate".into(),
        ));
    }

    let inner = WebPkiServerVerifier::builder_with_provider(Arc::new(roots), provider.clone())
        .build()
        .map_err(|e| Error::Certificate(e.to_string()))?;

    let config = ClientConfig::builder_with_provider(provider)
        .with_safe_default_protocol_versions()
        .map_err(|e| Error::Certificate(e.to_string()))?
        .dangerous()
        .with_custom_certificate_verifier(Arc::new(LoopbackNameVerifier { inner }))
        .with_no_client_auth();

    Ok(config)
}

#[cfg(test)]
mod tests {
    #[test]
    fn the_vendored_root_parses_and_builds_a_config() {
        // Guards the vendored PEM: a truncated or replaced file fails here
        // rather than at the first request on a player's machine.
        super::client_config().expect("Riot root should build a TLS config");
    }
}
