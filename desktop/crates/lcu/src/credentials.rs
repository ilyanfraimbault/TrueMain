//! Discovering where the running League client listens, and with what password.
//!
//! The client picks a fresh port and token every launch, so nothing here can be
//! cached across sessions. Two sources exist and they disagree in useful ways:
//!
//! * the **process arguments** of `LeagueClientUx`, which are authoritative for
//!   the client that is actually running;
//! * the **lockfile**, written into the install directory, which is easier to
//!   read but sits at a path the player can relocate.
//!
//! We try the arguments first and fall back to the lockfile: a stale lockfile
//! survives a crash, and trusting it would point us at a dead port.

use std::path::{Path, PathBuf};

use crate::error::{Error, Result};

/// Where the client listens and the password to get in.
#[derive(Clone, PartialEq, Eq)]
pub struct Credentials {
    pub port: u16,
    pub token: String,
}

impl Credentials {
    /// `Basic base64("riot:<token>")` — the username is always `riot`.
    pub fn authorization_header(&self) -> String {
        use base64::Engine as _;
        let raw = format!("riot:{}", self.token);
        format!(
            "Basic {}",
            base64::engine::general_purpose::STANDARD.encode(raw)
        )
    }

    pub fn base_url(&self) -> String {
        format!("https://127.0.0.1:{}", self.port)
    }
}

// Never print the token: these end up in logs, and it is a live credential for
// the player's own client.
impl std::fmt::Debug for Credentials {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Credentials")
            .field("port", &self.port)
            .field("token", &"<redacted>")
            .finish()
    }
}

/// Parse the lockfile's single line: `name:pid:port:password:protocol`.
///
/// The password is base64-ish and the process name can contain spaces, so we
/// split on `:` and read from the **left** by index rather than guessing.
pub fn parse_lockfile(contents: &str) -> Result<Credentials> {
    let line = contents.trim();
    let parts: Vec<&str> = line.split(':').collect();
    if parts.len() < 5 {
        return Err(Error::MalformedLockfile(format!(
            "expected 5 colon-separated fields, got {}",
            parts.len()
        )));
    }
    let port = parts[2]
        .parse::<u16>()
        .map_err(|_| Error::MalformedLockfile(format!("port {:?} is not a number", parts[2])))?;
    let token = parts[3].to_string();
    if token.is_empty() {
        return Err(Error::MalformedLockfile("empty password field".into()));
    }
    Ok(Credentials { port, token })
}

/// Pull `--app-port` and `--remoting-auth-token` out of a command line.
///
/// Written against the raw string rather than a parsed argv because the two
/// platforms hand us the command line in different shapes, and both quote it
/// differently; the flags themselves are stable.
pub fn parse_command_line(command_line: &str) -> Option<Credentials> {
    let port = extract_flag(command_line, "--app-port=")?
        .parse::<u16>()
        .ok()?;
    let token = extract_flag(command_line, "--remoting-auth-token=")?;
    if token.is_empty() {
        return None;
    }
    Some(Credentials { port, token })
}

/// Read one `--flag=value`, stopping at whitespace or a quote.
fn extract_flag(haystack: &str, flag: &str) -> Option<String> {
    let start = haystack.find(flag)? + flag.len();
    let rest = &haystack[start..];
    let end = rest
        .find(|c: char| c.is_whitespace() || c == '"' || c == '\'')
        .unwrap_or(rest.len());
    let value = &rest[..end];
    if value.is_empty() {
        None
    } else {
        Some(value.to_string())
    }
}

/// Install directories the client uses out of the box. The player can move the
/// installation, which is exactly why this is the *fallback* and not the
/// primary source.
fn default_lockfile_paths() -> Vec<PathBuf> {
    if cfg!(target_os = "macos") {
        vec![PathBuf::from(
            "/Applications/League of Legends.app/Contents/LoL/lockfile",
        )]
    } else if cfg!(target_os = "windows") {
        vec![
            PathBuf::from(r"C:\Riot Games\League of Legends\lockfile"),
            PathBuf::from(r"D:\Riot Games\League of Legends\lockfile"),
        ]
    } else {
        Vec::new()
    }
}

fn from_lockfile_at(path: &Path) -> Result<Credentials> {
    let contents = std::fs::read_to_string(path)?;
    parse_lockfile(&contents)
}

/// Ask the OS for the client's command line.
///
/// On Windows this deliberately does **not** use `wmic`: it is deprecated and
/// absent from recent Windows 11 builds, so the documented recipe found in most
/// LCU tutorials fails on a current machine. `Get-CimInstance` is the supported
/// replacement.
async fn command_line_from_process() -> Option<String> {
    let output = if cfg!(target_os = "windows") {
        tokio::process::Command::new("powershell")
            .args([
                "-NoProfile",
                "-Command",
                "Get-CimInstance Win32_Process -Filter \"name='LeagueClientUx.exe'\" \
                 | Select-Object -ExpandProperty CommandLine",
            ])
            .output()
            .await
            .ok()?
    } else {
        tokio::process::Command::new("ps")
            .args(["-A", "-o", "args="])
            .output()
            .await
            .ok()?
    };

    let stdout = String::from_utf8_lossy(&output.stdout);
    stdout
        .lines()
        .find(|line| line.contains("LeagueClientUx") && line.contains("--remoting-auth-token="))
        .map(str::to_string)
}

/// Find the running client, arguments first, lockfile second.
///
/// Returns [`Error::ClientNotRunning`] rather than a generic failure when both
/// sources come up empty — the caller needs to tell "no client" apart from
/// "something broke", because the first is the normal state half the time.
pub async fn discover() -> Result<Credentials> {
    if let Some(command_line) = command_line_from_process().await {
        if let Some(credentials) = parse_command_line(&command_line) {
            return Ok(credentials);
        }
    }

    for path in default_lockfile_paths() {
        if let Ok(credentials) = from_lockfile_at(&path) {
            return Ok(credentials);
        }
    }

    Err(Error::ClientNotRunning)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_port_and_password_from_a_lockfile() {
        let credentials = parse_lockfile("LeagueClient:34604:52847:aBcDeF-1234:https").unwrap();
        assert_eq!(credentials.port, 52847);
        assert_eq!(credentials.token, "aBcDeF-1234");
    }

    #[test]
    fn tolerates_the_trailing_newline_the_client_writes() {
        assert!(parse_lockfile("LeagueClient:1:2:tok:https\n").is_ok());
    }

    #[test]
    fn rejects_a_truncated_lockfile_rather_than_guessing() {
        // A half-written lockfile is a real state: the client writes it at
        // launch and we can read it mid-write.
        assert!(parse_lockfile("LeagueClient:34604:52847").is_err());
    }

    #[test]
    fn rejects_a_lockfile_with_no_password() {
        assert!(parse_lockfile("LeagueClient:1:2::https").is_err());
    }

    #[test]
    fn reads_credentials_from_a_macos_command_line() {
        let line = "/Applications/League of Legends.app/Contents/LoL/LeagueClientUx.app/Contents/MacOS/LeagueClientUx \
                    --riotclient-app-port=51234 --app-port=52847 --remoting-auth-token=aBcDeF-1234 --locale=en_GB";
        let credentials = parse_command_line(line).unwrap();
        assert_eq!(credentials.port, 52847);
        assert_eq!(credentials.token, "aBcDeF-1234");
    }

    #[test]
    fn reads_credentials_from_a_quoted_windows_command_line() {
        let line = r#""C:\Riot Games\League of Legends\LeagueClientUx.exe" "--app-port=52847" "--remoting-auth-token=aBcDeF-1234""#;
        let credentials = parse_command_line(line).unwrap();
        assert_eq!(credentials.port, 52847);
        assert_eq!(credentials.token, "aBcDeF-1234");
    }

    #[test]
    fn does_not_confuse_the_riot_client_port_with_the_league_one() {
        // Both flags end in `-app-port=`; a naive `find` on "app-port=" picks
        // the Riot Client's port and every later request 404s.
        let line =
            "LeagueClientUx --riotclient-app-port=51234 --app-port=52847 --remoting-auth-token=tok";
        assert_eq!(parse_command_line(line).unwrap().port, 52847);
    }

    #[test]
    fn returns_nothing_when_the_token_flag_is_absent() {
        assert!(parse_command_line("LeagueClientUx --app-port=52847").is_none());
    }

    #[test]
    fn builds_the_basic_auth_header_the_client_expects() {
        let credentials = Credentials {
            port: 1,
            token: "secret".into(),
        };
        // base64("riot:secret")
        assert_eq!(credentials.authorization_header(), "Basic cmlvdDpzZWNyZXQ=");
    }

    #[test]
    fn never_prints_the_token() {
        let credentials = Credentials {
            port: 1,
            token: "secret".into(),
        };
        let rendered = format!("{credentials:?}");
        assert!(
            !rendered.contains("secret"),
            "token leaked into Debug output"
        );
    }
}
