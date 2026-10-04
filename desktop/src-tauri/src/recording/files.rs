//! The `recording` URI scheme: the recordings folder's videos and thumbnails
//! as URLs the webview may load (`convertFileSrc(path, 'recording')`), and
//! nothing outside that folder.
//!
//! Tauri's own asset protocol answers every range with at most 1000 KiB.
//! WebKit's MP4 reader asks for each box of the `moov` in one explicit range
//! and drops the track when the answer comes back short — and a video's
//! sample table passes 1000 KiB at about 36 minutes of 60 fps, so a long
//! game played as a black screen with its sound (#1830). An explicit range is
//! therefore answered in full, up to a bound no real box reaches; only an
//! open-ended one is cut into chunks, which every player follows.

use std::fs::File;
use std::io::{Read, Seek, SeekFrom};
use std::path::{Path, PathBuf};

use tauri::http::{header, Method, Request, Response, StatusCode};

pub const SCHEME: &str = "recording";

/// The most an explicit range is answered with.
const MAX_EXPLICIT: u64 = 64 * 1024 * 1024;
/// What an open-ended range (`bytes=N-`) or a plain GET of a large file gets.
const CHUNK: u64 = 2 * 1024 * 1024;

/// The bytes a request asks for, as `start..=end` of a file of `len` bytes.
#[derive(Debug, PartialEq, Eq)]
enum Wanted {
    Whole,
    Range { start: u64, end: u64 },
    Unsatisfiable,
}

/// Read one `Range` header against a file of `len` bytes, already bounded.
/// Several ranges in one header are answered with the first.
fn wanted(range: Option<&str>, len: u64) -> Wanted {
    let Some(range) = range else {
        // A plain GET of a video would read gigabytes: answer it as a range.
        return if len > CHUNK {
            Wanted::Range {
                start: 0,
                end: CHUNK - 1,
            }
        } else {
            Wanted::Whole
        };
    };
    let Some(spec) = range.trim().strip_prefix("bytes=") else {
        return Wanted::Unsatisfiable;
    };
    let spec = spec.split(',').next().unwrap_or_default().trim();
    let Some((from, to)) = spec.split_once('-') else {
        return Wanted::Unsatisfiable;
    };
    if len == 0 {
        return Wanted::Unsatisfiable;
    }
    let (start, end) = match (from.trim(), to.trim()) {
        // The last `n` bytes.
        ("", n) => match n.parse::<u64>() {
            Ok(n) if n > 0 => (len.saturating_sub(n), len - 1),
            _ => return Wanted::Unsatisfiable,
        },
        (a, "") => match a.parse::<u64>() {
            Ok(a) => (a, a.saturating_add(CHUNK - 1)),
            Err(_) => return Wanted::Unsatisfiable,
        },
        (a, b) => match (a.parse::<u64>(), b.parse::<u64>()) {
            (Ok(a), Ok(b)) if a <= b => (a, b.min(a.saturating_add(MAX_EXPLICIT - 1))),
            _ => return Wanted::Unsatisfiable,
        },
    };
    if start >= len {
        return Wanted::Unsatisfiable;
    }
    Wanted::Range {
        start,
        end: end.min(len - 1),
    }
}

/// The file a URL names, when it lies inside `folder`. The path is the URL's,
/// percent-decoded; both sides are resolved so `..` and links cannot leave it.
fn resolve(url_path: &str, folder: &Path) -> Option<PathBuf> {
    let decoded = percent_encoding::percent_decode_str(url_path.trim_start_matches('/'))
        .decode_utf8()
        .ok()?;
    let file = Path::new(decoded.as_ref()).canonicalize().ok()?;
    let folder = folder.canonicalize().ok()?;
    (file.starts_with(&folder) && file.is_file()).then_some(file)
}

fn content_type(path: &Path) -> &'static str {
    match path
        .extension()
        .and_then(|e| e.to_str())
        .map(str::to_ascii_lowercase)
        .as_deref()
    {
        Some("mp4") => "video/mp4",
        Some("jpg" | "jpeg") => "image/jpeg",
        Some("png") => "image/png",
        _ => "application/octet-stream",
    }
}

fn status(code: StatusCode) -> Response<Vec<u8>> {
    let mut response = Response::new(Vec::new());
    *response.status_mut() = code;
    response
}

/// Answer one request for a file of `folder`.
pub fn serve(request: &Request<Vec<u8>>, folder: &Path) -> Response<Vec<u8>> {
    let Some(path) = resolve(request.uri().path(), folder) else {
        return status(StatusCode::FORBIDDEN);
    };
    match read(request, &path) {
        Ok(response) => response,
        Err(error) => {
            tracing::warn!(%error, path = %path.display(), "recording file unreadable");
            status(StatusCode::INTERNAL_SERVER_ERROR)
        }
    }
}

fn read(request: &Request<Vec<u8>>, path: &Path) -> std::io::Result<Response<Vec<u8>>> {
    let mut file = File::open(path)?;
    let len = file.metadata()?.len();
    let range = request
        .headers()
        .get(header::RANGE)
        .and_then(|value| value.to_str().ok());
    let builder = Response::builder()
        .header(header::CONTENT_TYPE, content_type(path))
        .header(header::ACCEPT_RANGES, "bytes");
    let head = request.method() == Method::HEAD;

    let response = match wanted(range, len) {
        Wanted::Unsatisfiable => builder
            .status(StatusCode::RANGE_NOT_SATISFIABLE)
            .header(header::CONTENT_RANGE, format!("bytes */{len}"))
            .body(Vec::new()),
        Wanted::Whole => {
            let mut body = Vec::with_capacity(len as usize);
            if !head {
                file.read_to_end(&mut body)?;
            }
            builder
                .status(StatusCode::OK)
                .header(header::CONTENT_LENGTH, len)
                .body(body)
        }
        Wanted::Range { start, end } => {
            let count = end - start + 1;
            let mut body = Vec::with_capacity(count as usize);
            if !head {
                file.seek(SeekFrom::Start(start))?;
                file.take(count).read_to_end(&mut body)?;
            }
            builder
                .status(StatusCode::PARTIAL_CONTENT)
                .header(header::CONTENT_RANGE, format!("bytes {start}-{end}/{len}"))
                .header(header::CONTENT_LENGTH, count)
                .body(body)
        }
    };
    response.map_err(std::io::Error::other)
}

#[cfg(test)]
mod tests {
    use super::*;

    const GB: u64 = 1024 * 1024 * 1024;

    #[test]
    fn answers_an_explicit_range_past_a_mebibyte_in_full() {
        // The 43-minute game's video sample table.
        assert_eq!(
            wanted(Some("bytes=7213139989-7214299612"), 7 * GB),
            Wanted::Range {
                start: 7_213_139_989,
                end: 7_214_299_612
            }
        );
    }

    #[test]
    fn bounds_a_huge_explicit_range() {
        assert_eq!(
            wanted(Some("bytes=0-999999999999"), 7 * GB),
            Wanted::Range {
                start: 0,
                end: MAX_EXPLICIT - 1
            }
        );
    }

    #[test]
    fn chunks_an_open_range_and_a_plain_get_of_a_video() {
        assert_eq!(
            wanted(Some("bytes=100-"), 7 * GB),
            Wanted::Range {
                start: 100,
                end: 100 + CHUNK - 1
            }
        );
        assert_eq!(
            wanted(None, 7 * GB),
            Wanted::Range {
                start: 0,
                end: CHUNK - 1
            }
        );
    }

    #[test]
    fn sends_a_thumbnail_whole() {
        assert_eq!(wanted(None, 66_362), Wanted::Whole);
    }

    #[test]
    fn clamps_to_the_end_of_the_file() {
        assert_eq!(
            wanted(Some("bytes=90-200"), 100),
            Wanted::Range { start: 90, end: 99 }
        );
        assert_eq!(
            wanted(Some("bytes=-10"), 100),
            Wanted::Range { start: 90, end: 99 }
        );
        assert_eq!(
            wanted(Some("bytes=-500"), 100),
            Wanted::Range { start: 0, end: 99 }
        );
    }

    #[test]
    fn refuses_what_cannot_be_served() {
        assert_eq!(wanted(Some("bytes=100-"), 100), Wanted::Unsatisfiable);
        assert_eq!(wanted(Some("bytes=20-10"), 100), Wanted::Unsatisfiable);
        assert_eq!(wanted(Some("bytes=-0"), 100), Wanted::Unsatisfiable);
        assert_eq!(wanted(Some("items=0-10"), 100), Wanted::Unsatisfiable);
        assert_eq!(wanted(Some("bytes=x-1"), 100), Wanted::Unsatisfiable);
        assert_eq!(wanted(Some("bytes=0-1"), 0), Wanted::Unsatisfiable);
    }

    #[test]
    fn keeps_to_the_recordings_folder() {
        let root = std::env::temp_dir().join(format!("truemain-files-{}", std::process::id()));
        let folder = root.join("recordings");
        std::fs::create_dir_all(folder.join("8003493673")).unwrap();
        std::fs::write(folder.join("8003493673/video.mp4"), b"mp4").unwrap();
        std::fs::write(root.join("secret.txt"), b"no").unwrap();
        let url = |path: &Path| {
            format!(
                "/{}",
                percent_encoding::utf8_percent_encode(
                    path.to_str().unwrap(),
                    percent_encoding::NON_ALPHANUMERIC
                )
            )
        };

        let video = folder.join("8003493673/video.mp4");
        assert_eq!(
            resolve(&url(&video), &folder),
            Some(video.canonicalize().unwrap())
        );
        assert_eq!(resolve(&url(&root.join("secret.txt")), &folder), None);
        assert_eq!(resolve(&url(&folder.join("../secret.txt")), &folder), None);
        assert_eq!(resolve(&url(&folder.join("8003493673")), &folder), None);
        assert_eq!(resolve(&url(&folder.join("missing.mp4")), &folder), None);

        std::fs::remove_dir_all(&root).unwrap();
    }
}
