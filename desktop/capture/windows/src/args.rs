//! The command line, read the way the macOS helper reads it: `--name value`
//! pairs in any order, a bad value ending the helper with a `usage` error.

use crate::output::fail;

pub fn value<'a>(name: &str, arguments: &'a [String]) -> Option<&'a str> {
    let index = arguments.iter().position(|argument| argument == name)?;
    arguments.get(index + 1).map(String::as_str)
}

pub fn has(name: &str, arguments: &[String]) -> bool {
    arguments.iter().any(|argument| argument == name)
}

pub fn required<'a>(name: &str, arguments: &'a [String]) -> &'a str {
    value(name, arguments).unwrap_or_else(|| fail("usage", &format!("{name} is required"), 1))
}

/// A positive number.
pub fn integer(name: &str, arguments: &[String]) -> u32 {
    match value(name, arguments).and_then(|raw| raw.parse::<u32>().ok()) {
        Some(number) if number > 0 => number,
        _ => fail("usage", &format!("{name} needs a positive number"), 1),
    }
}

/// A number of milliseconds, zero allowed.
pub fn milliseconds(name: &str, arguments: &[String]) -> u64 {
    value(name, arguments)
        .and_then(|raw| raw.parse::<u64>().ok())
        .unwrap_or_else(|| {
            fail(
                "usage",
                &format!("{name} needs a number of milliseconds"),
                1,
            )
        })
}

pub fn window_id(arguments: &[String]) -> Option<u32> {
    value("--window-id", arguments).and_then(|raw| raw.parse().ok())
}

/// What to capture: the game's window alone, or the monitor it is on.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Source {
    Window,
    Display,
}

impl Source {
    pub fn parse(arguments: &[String]) -> Self {
        match value("--source", arguments) {
            None | Some("window") => Self::Window,
            Some("display") => Self::Display,
            Some(_) => fail("usage", "--source is window or display", 1),
        }
    }

    pub fn name(self) -> &'static str {
        match self {
            Self::Window => "window",
            Self::Display => "display",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Codec {
    H264,
    Hevc,
}

impl Codec {
    pub fn parse(arguments: &[String]) -> Self {
        match value("--codec", arguments) {
            None | Some("h264") => Self::H264,
            Some("hevc") => Self::Hevc,
            Some(_) => fail("usage", "--codec is h264 or hevc", 1),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn args(line: &str) -> Vec<String> {
        line.split_whitespace().map(String::from).collect()
    }

    #[test]
    fn values_are_read_by_name_in_any_order() {
        let arguments = args("record --fps 30 --out C:\\game.mp4 --no-audio");
        assert_eq!(value("--out", &arguments), Some("C:\\game.mp4"));
        assert_eq!(integer("--fps", &arguments), 30);
        assert!(has("--no-audio", &arguments));
        assert_eq!(value("--width", &arguments), None);
        assert_eq!(value("--no-audio", &arguments), None);
    }

    #[test]
    fn source_and_codec_default_like_the_macos_helper() {
        assert_eq!(Source::parse(&args("probe")), Source::Window);
        assert_eq!(
            Source::parse(&args("probe --source display")),
            Source::Display
        );
        assert_eq!(Codec::parse(&args("record")), Codec::H264);
        assert_eq!(Codec::parse(&args("record --codec hevc")), Codec::Hevc);
        assert_eq!(window_id(&args("probe --window-id 65874")), Some(65874));
    }
}
