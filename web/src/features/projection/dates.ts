const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** A date as a statement prints it, 28 Jun 2025. A period end is a calendar date, read in UTC; a moment is read in the user's own zone. */
export const printDate = (value: string, zone: "utc" | "local") => {
  const d = new Date(value);
  return zone === "utc"
    ? `${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]} ${d.getUTCFullYear()}`
    : `${d.getDate()} ${MONTHS[d.getMonth()]} ${d.getFullYear()}`;
};
