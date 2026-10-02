//! BGRA to NV12 on the CPU, scaled to fit and centred on black — the
//! fallback where the GPU has no video processor (no driver, a virtual
//! machine, a CI runner). BT.709, studio range, like the GPU path.

/// `content` scaled to fit `output`, centred, on even pixels (NV12 halves
/// the chroma): left, top, width, height.
pub fn fit(content: (u32, u32), output: (u32, u32)) -> (u32, u32, u32, u32) {
    let scale = f64::min(
        f64::from(output.0) / f64::from(content.0.max(1)),
        f64::from(output.1) / f64::from(content.1.max(1)),
    );
    let width = ((f64::from(content.0) * scale) as u32 & !1).clamp(2, output.0);
    let height = ((f64::from(content.1) * scale) as u32 & !1).clamp(2, output.1);
    let left = ((output.0 - width) / 2) & !1;
    let top = ((output.1 - height) / 2) & !1;
    (left, top, width, height)
}

/// The size of an NV12 picture: a full-size luma plane, then interleaved
/// chroma at half size both ways.
pub fn len(size: (u32, u32)) -> usize {
    size.0 as usize * size.1 as usize * 3 / 2
}

fn luma(b: u8, g: u8, r: u8) -> u8 {
    ((47 * u32::from(r) + 157 * u32::from(g) + 16 * u32::from(b) + 4096 + 128) >> 8) as u8
}

fn chroma(b: u32, g: u32, r: u32) -> (u8, u8) {
    let (b, g, r) = (b as i32, g as i32, r as i32);
    let u = (-26 * r - 86 * g + 112 * b + 32768 + 128) >> 8;
    let v = (112 * r - 102 * g - 10 * b + 32768 + 128) >> 8;
    (u.clamp(0, 255) as u8, v.clamp(0, 255) as u8)
}

/// Draw `source` (`size`, rows `pitch` bytes apart) into `out`, an NV12
/// picture of `output`. Nearest-neighbour scaling: this path is for
/// machines that cannot do better, not for quality.
pub fn convert(source: &[u8], pitch: usize, size: (u32, u32), out: &mut [u8], output: (u32, u32)) {
    let (width, height) = (output.0 as usize, output.1 as usize);
    let (luma_plane, chroma_plane) = out.split_at_mut(width * height);
    luma_plane.fill(16);
    chroma_plane.fill(128);
    let (left, top, fit_width, fit_height) = fit(size, output);
    let (left, top, fit_width, fit_height) = (
        left as usize,
        top as usize,
        fit_width as usize,
        fit_height as usize,
    );
    let column = |x: usize| (x - left) * size.0 as usize / fit_width;
    let row = |y: usize| (y - top) * size.1 as usize / fit_height;
    let pixel = |x: usize, y: usize| {
        let at = row(y) * pitch + column(x) * 4;
        (source[at], source[at + 1], source[at + 2])
    };

    for y in top..top + fit_height {
        let line = &mut luma_plane[y * width..(y + 1) * width];
        for (x, value) in line.iter_mut().enumerate().skip(left).take(fit_width) {
            let (b, g, r) = pixel(x, y);
            *value = luma(b, g, r);
        }
    }
    for y in (top..top + fit_height).step_by(2) {
        let line = &mut chroma_plane[(y / 2) * width..(y / 2 + 1) * width];
        for x in (left..left + fit_width).step_by(2) {
            let (mut b, mut g, mut r) = (0u32, 0u32, 0u32);
            for (dx, dy) in [(0, 0), (1, 0), (0, 1), (1, 1)] {
                let (pb, pg, pr) = pixel(x + dx, y + dy);
                b += u32::from(pb);
                g += u32::from(pg);
                r += u32::from(pr);
            }
            let (u, v) = chroma(b / 4, g / 4, r / 4);
            line[x] = u;
            line[x + 1] = v;
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn solid(size: (u32, u32), bgra: [u8; 4]) -> Vec<u8> {
        bgra.repeat(size.0 as usize * size.1 as usize)
    }

    #[test]
    fn a_wider_picture_is_letterboxed() {
        assert_eq!(fit((1920, 1080), (1280, 720)), (0, 0, 1280, 720));
        assert_eq!(fit((1000, 1000), (1280, 720)), (280, 0, 720, 720));
        assert_eq!(fit((946, 533), (1280, 720)), (2, 0, 1276, 720));
    }

    #[test]
    fn white_and_black_land_on_the_studio_range() {
        let output = (4, 2);
        let mut out = vec![0; len(output)];
        convert(
            &solid((4, 2), [255, 255, 255, 255]),
            16,
            (4, 2),
            &mut out,
            output,
        );
        assert!(out[..8].iter().all(|&y| y == 235), "{out:?}");
        assert!(out[8..].iter().all(|&c| c == 128), "{out:?}");

        convert(&solid((4, 2), [0, 0, 0, 255]), 16, (4, 2), &mut out, output);
        assert!(out[..8].iter().all(|&y| y == 16), "{out:?}");
    }

    #[test]
    fn red_reads_as_red() {
        let output = (2, 2);
        let mut out = vec![0; len(output)];
        convert(
            &solid((2, 2), [0, 0, 255, 255]),
            8,
            (2, 2),
            &mut out,
            output,
        );
        // BT.709 studio-range red: Y 63, Cb 102, Cr 240.
        assert_eq!(out, vec![63, 63, 63, 63, 102, 240]);
    }

    #[test]
    fn the_sides_stay_black_and_the_pitch_is_honoured() {
        // A 2x2 white square, rows padded to 16 bytes, into a 6x2 picture.
        let mut source = vec![0u8; 16 * 2];
        for row in 0..2 {
            source[row * 16..row * 16 + 8].fill(255);
        }
        let output = (6, 2);
        let mut out = vec![0; len(output)];
        convert(&source, 16, (2, 2), &mut out, output);
        assert_eq!(&out[..6], &[16, 16, 235, 235, 16, 16]);
    }
}
