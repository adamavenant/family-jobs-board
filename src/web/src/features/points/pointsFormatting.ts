/** Signed points for one ledger entry, using a true minus sign: "+5" or "−3". */
export function formatPoints(points: number): string {
  return points < 0 ? `−${Math.abs(points)}` : `+${points}`;
}

/** A points balance, using a true minus sign when it is below zero. */
export function formatBalance(balance: number): string {
  return balance < 0 ? `−${Math.abs(balance)}` : String(balance);
}

export function formatAwardTime(value: string): string {
  return new Intl.DateTimeFormat("en", {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value));
}
