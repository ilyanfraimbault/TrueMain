/**
 * `11697` → `11.7k`: the scoreboard's damage and team-gold figures. The bar or
 * the side already carries the comparison, the label only gives the order of
 * magnitude, and a fixed one-decimal form keeps a column's labels the same width.
 */
export function formatKilo(value: number): string {
  return value >= 1000 ? `${(value / 1000).toFixed(1)}k` : `${value}`
}
