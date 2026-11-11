// GLSL sources of AppBackdrop's single full-screen pass: a vertex shader that
// covers the viewport with one triangle, and the fragment shader that draws the
// eclipse (or, with u_starsOnly, the star field alone). The component owns the
// WebGL lifecycle and feeds the uniforms declared here.

export const BACKDROP_VERTEX_SHADER = `
attribute vec2 a_position;
void main() {
  gl_Position = vec4(a_position, 0.0, 1.0);
}
`

export const BACKDROP_FRAGMENT_SHADER = `
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif
uniform vec2 u_resolution;
uniform float u_time;
uniform vec2 u_pointer;   // smoothed cursor, viewport fractions (0..1, y up)
uniform float u_pointerK; // 0 = ambient drift, 1 = cursor-driven
uniform float u_starsOnly; // 1 = variant="stars"
uniform float u_refH;      // the height space is normalised by (device px)

float hash2(vec2 p) {
  return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453);
}

float vnoise(vec2 p) {
  vec2 i = floor(p), f = fract(p);
  float a = hash2(i), b = hash2(i + vec2(1.0, 0.0));
  float c = hash2(i + vec2(0.0, 1.0)), d = hash2(i + vec2(1.0, 1.0));
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p) {
  float v = 0.0, a = 0.5;
  for (int i = 0; i < 3; i++) { v += a * vnoise(p); p = p * 2.03 + vec2(7.3, 3.1); a *= 0.5; }
  return v;
}

// Viewport fractions -> the same height-normalised space as p, so anchors
// keep their on-screen position on any aspect ratio. Normalising by height
// (not min(w,h)) is deliberate: the hero text is vertically centred and
// height-driven, so tying the orb's size and vertical position to height too
// keeps them locked together — resizing width no longer shrinks or shifts
// the eclipse relative to the text. In landscape this equals min(w,h), so
// the common case is unchanged.
vec2 anchor(vec2 uv) {
  return (uv * 2.0 - 1.0) * u_resolution / u_refH;
}

vec2 starPos(vec2 g) {
  return g + 0.2 + 0.6 * vec2(hash2(g), hash2(g + 17.3));
}

void main() {
  vec2 p = (gl_FragCoord.xy * 2.0 - u_resolution) / u_refH;
  float t = u_time;

  const vec3 WINE = vec3(0.30, 0.09, 0.15);
  const vec3 ROSE = vec3(0.80, 0.36, 0.32);
  const vec3 CHAMPAGNE = vec3(0.95, 0.70, 0.48);

  // The orb drifts on two superposed slow oscillations — never still, but
  // barely wandering: the amplitudes are deliberately small so it sits close
  // to upper-center and the motion reads as a gentle breath, not travel. The
  // reading shield below stays put, so the text zone is protected wherever the
  // rim lands.
  vec2 c = anchor(vec2(
    0.5 + 0.010 * sin(t * 0.19) + 0.003 * sin(t * 0.53 + 1.3),
    0.68 + 0.012 * sin(t * 0.14 + 2.1) + 0.003 * sin(t * 0.47)));
  vec2 d2c = p - c;
  float d = length(d2c);
  float R = 0.38;
  vec2 dirN = d2c / max(d, 1e-4);

  // Rim + corona (fbm sampled on the unit circle so there is no angle seam).
  float rim = exp(-abs(d - R) * 30.0);
  float outer = max(d - R, 0.0);
  float streams = pow(max(fbm(dirN * 1.9 + outer * 2.0 + vec2(0.0, t * 0.04)) - 0.28, 0.0), 1.8);
  float cor = exp(-outer * 3.4) * streams * step(R, d);

  // Cursor flare: the corona surges on the side of the orb facing the mouse.
  vec2 cpDir = anchor(u_pointer) - c;
  cpDir /= max(length(cpDir), 1e-4);
  float facing = pow(max(dot(dirN, cpDir), 0.0), 5.0);
  cor *= 0.6 + 1.5 * u_pointerK * facing;
  rim *= 0.75 + 0.8 * u_pointerK * facing;

  float eclipse = 1.0 - u_starsOnly;
  float inside = smoothstep(R, R - 0.025, d) * eclipse;
  vec3 col = (ROSE * 0.7 + CHAMPAGNE * 0.5) * rim * eclipse * (1.0 - inside * 0.6);
  col += (WINE * 0.7 + ROSE * 0.55) * cor * eclipse * 1.5;

  // Natural aura: a wide, very soft atmosphere breathing out from the disc,
  // slower and calmer than the corona streams. Outward only — the disc's
  // interior is the hero's reading surface and must stay dark.
  float breathe = 0.85 + 0.15 * sin(t * 0.25);
  col += (WINE * 0.75 + ROSE * 0.3) * exp(-outer * 5.5) * step(R, d) * 0.2 * breathe * eclipse;
  // Earthshine: the sphere's limb catches a whisper of that light just
  // inside the rim — hugging the edge so it never veils the text.
  col += (WINE * 0.6 + ROSE * 0.3) * exp(-(R - d) * 18.0) * inside * 0.3 * breathe;

  // Sparse stars, hidden behind the disc.
  vec2 q = p * 4.0 + 11.0;
  vec2 g = floor(q);
  float st = 0.0;
  for (int dy = -1; dy <= 1; dy++) {
    for (int dx = -1; dx <= 1; dx++) {
      vec2 gg = g + vec2(float(dx), float(dy));
      float h = hash2(gg + 5.1);
      float tw = 0.5 + 0.5 * sin(t * (0.5 + h) + h * 40.0);
      st += exp(-dot(q - starPos(gg), q - starPos(gg)) * 1000.0) * tw * step(0.55, h);
    }
  }
  col += mix(ROSE, CHAMPAGNE, 0.6) * st * 0.5 * (1.0 - inside);

  // Reading shield: gently dim whatever crosses the hero text zone, so the
  // title keeps its contrast whichever way the rim lands on this screen.
  vec2 hz = (p - anchor(vec2(0.5, 0.55))) / vec2(0.72, 0.34);
  col *= 1.0 - 0.6 * (1.0 - smoothstep(0.75, 1.3, length(hz))) * eclipse;

  // Fine animated grain keeps the soft gradients from banding.
  col += (hash2(gl_FragCoord.xy + fract(t) * 61.0) - 0.5) * 0.02;
  col = max(col, 0.0);

  // Premultiplied-alpha output: colour carries its own intensity and alpha =
  // luminance, composited as src + dst*(1-a) — same additive-over-the-page
  // result as the previous unpremultiplied form, but iOS Safari ignores
  // premultipliedAlpha:false and composited the normalised (full-brightness)
  // colours directly, flooding the page rose and turning the grain into
  // static. Premultiplied is honoured everywhere.
  float lum = max(col.r, max(col.g, col.b));
  float alpha = clamp(lum, 0.0, 0.95);
  gl_FragColor = vec4(col * (alpha / max(lum, 1e-4)), alpha);
}
`
