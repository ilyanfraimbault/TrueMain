//! A published clip whose video left the disk plays from YouTube's embedded
//! player (#2005) — but not from the app's own page: YouTube's player refuses
//! to play without a `Referer` (error 153), and a page on a custom scheme, as
//! the app's `tauri://localhost` on macOS, sends none.
//!
//! So the shell serves the one page the embed needs on the loopback interface,
//! and the clip player frames it: from `http://127.0.0.1:<port>`, the embed's
//! request carries a referrer YouTube accepts. The server answers
//! `GET /youtube/<video id>` and nothing else, binds on first use, and lives as
//! long as the app.

use std::io::{Read, Write};
use std::net::{TcpListener, TcpStream};
use std::sync::OnceLock;
use std::time::Duration;

use game_recording::clips::is_video_id;

const READ_TIMEOUT: Duration = Duration::from_secs(2);
const MAX_REQUEST_BYTES: usize = 4096;

static PORT: OnceLock<Result<u16, String>> = OnceLock::new();

/// The URL of the page embedding the YouTube video `id`.
pub fn page_url(id: &str) -> Result<String, String> {
    let port = PORT.get_or_init(start).clone()?;
    Ok(format!("http://127.0.0.1:{port}/youtube/{id}"))
}

fn start() -> Result<u16, String> {
    let listener = TcpListener::bind(("127.0.0.1", 0)).map_err(|error| error.to_string())?;
    let port = listener
        .local_addr()
        .map_err(|error| error.to_string())?
        .port();
    std::thread::Builder::new()
        .name("youtube-embed".into())
        .spawn(move || {
            for stream in listener.incoming().flatten() {
                if let Err(error) = answer(stream) {
                    tracing::debug!(%error, "youtube embed request");
                }
            }
        })
        .map_err(|error| error.to_string())?;
    Ok(port)
}

fn answer(mut stream: TcpStream) -> std::io::Result<()> {
    stream.set_read_timeout(Some(READ_TIMEOUT))?;
    let mut request = Vec::new();
    let mut chunk = [0u8; 1024];
    while !request.windows(4).any(|window| window == b"\r\n\r\n")
        && request.len() < MAX_REQUEST_BYTES
    {
        let read = stream.read(&mut chunk)?;
        if read == 0 {
            break;
        }
        request.extend_from_slice(&chunk[..read]);
    }
    let request = String::from_utf8_lossy(&request);
    let response = match video_id(request.lines().next().unwrap_or_default()) {
        Some(id) => ok(&page(id)),
        None => not_found(),
    };
    stream.write_all(response.as_bytes())
}

/// The video id of `GET /youtube/<id> HTTP/1.x`, the one request answered.
fn video_id(request_line: &str) -> Option<&str> {
    let mut parts = request_line.split(' ');
    let (method, target) = (parts.next()?, parts.next()?);
    let id = target.strip_prefix("/youtube/")?;
    (method == "GET" && is_video_id(id)).then_some(id)
}

fn page(id: &str) -> String {
    format!(
        "<!doctype html><html><head><meta charset=\"utf-8\">\
<style>html,body{{margin:0;height:100%;background:#000;overflow:hidden}}\
iframe{{border:0;width:100%;height:100%}}</style></head><body>\
<iframe src=\"https://www.youtube-nocookie.com/embed/{id}?rel=0\" \
referrerpolicy=\"strict-origin-when-cross-origin\" \
allow=\"autoplay; encrypted-media; picture-in-picture; fullscreen\" allowfullscreen></iframe>\
</body></html>"
    )
}

fn ok(body: &str) -> String {
    format!(
        "HTTP/1.1 200 OK\r\n\
Content-Type: text/html; charset=utf-8\r\n\
Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; frame-src https://www.youtube-nocookie.com\r\n\
Referrer-Policy: strict-origin-when-cross-origin\r\n\
Cache-Control: no-store\r\n\
Content-Length: {}\r\n\
Connection: close\r\n\r\n{body}",
        body.len()
    )
}

fn not_found() -> String {
    "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n".into()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn answers_a_youtube_video_only() {
        assert_eq!(
            video_id("GET /youtube/dQw4w9WgXcQ HTTP/1.1"),
            Some("dQw4w9WgXcQ")
        );
        assert_eq!(video_id("POST /youtube/dQw4w9WgXcQ HTTP/1.1"), None);
        assert_eq!(video_id("GET /youtube/short HTTP/1.1"), None);
        assert_eq!(video_id("GET /youtube/dQw4w9WgXcQ/x HTTP/1.1"), None);
        assert_eq!(video_id("GET /etc/passwd HTTP/1.1"), None);
        assert_eq!(video_id(""), None);
    }

    #[test]
    fn serves_the_embed_page_over_the_loopback() {
        let url = page_url("dQw4w9WgXcQ").unwrap();
        let address = url
            .strip_prefix("http://")
            .and_then(|rest| rest.split('/').next())
            .unwrap();
        let mut stream = TcpStream::connect(address).unwrap();
        stream
            .write_all(b"GET /youtube/dQw4w9WgXcQ HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")
            .unwrap();
        let mut response = String::new();
        stream.read_to_string(&mut response).unwrap();
        assert!(response.starts_with("HTTP/1.1 200 OK"));
        assert!(response.contains("youtube-nocookie.com/embed/dQw4w9WgXcQ"));
    }
}
