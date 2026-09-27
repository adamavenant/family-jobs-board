import { useState } from "react";
import { Link, useFetcher, useLoaderData } from "react-router";

import type { PointsLedger, PointsLedgerEntry } from "../../api/pointsLedger";
import { ThemeToggle } from "../theme/ThemeToggle";
import { IdentityControls } from "../today/TodayPage";
import {
  formatAwardTime,
  formatBalance,
  formatPoints,
} from "./pointsFormatting";
import { pointsHref } from "./pointsLedgerRoute";
import type { PointsLedgerLoaderData } from "./pointsLedgerRoute";

export function PointsLedgerPage() {
  const data = useLoaderData() as PointsLedgerLoaderData;
  const isAdult = data.viewer.role === "adult";
  const ownBalance =
    !isAdult && data.state === "ready"
      ? (data.ledger.children[0]?.balance ?? 0)
      : null;

  return (
    <main>
      <header className="hero">
        <div className="hero__toolbar">
          <p className="eyebrow">Family Jobs Board</p>
          <div className="hero__actions">
            <IdentityControls viewer={data.viewer} />
            <ThemeToggle />
          </div>
        </div>
        <div className="hero__content">
          <div>
            <h1>{isAdult ? "Points" : "Your points"}</h1>
            <p className="hero__date">
              <Link to="/">← Back to today</Link>
            </p>
          </div>
          {ownBalance === null ? null : (
            <div className="hero__stats">
              <div
                className="hero__balance"
                aria-label={`${formatBalance(ownBalance)} points earned`}
              >
                <strong>{formatBalance(ownBalance)}</strong>
                <span>points earned</span>
              </div>
            </div>
          )}
        </div>
      </header>

      {data.state === "error" ? (
        <p className="error-message points-ledger__error" role="alert">
          {data.error}
        </p>
      ) : (
        <section
          className="board points-ledger"
          aria-labelledby="ledger-heading"
        >
          {isAdult ? <ChildFilter ledger={data.ledger} /> : null}
          <LedgerEntries
            // A new first page (another filter, or new points) starts the list again.
            key={`${data.ledger.selectedChildId ?? "all"}:${data.ledger.entries[0]?.id ?? "empty"}`}
            ledger={data.ledger}
            isAdult={isAdult}
            viewerName={data.viewer.displayName}
          />
        </section>
      )}
    </main>
  );
}

function ChildFilter({ ledger }: { ledger: PointsLedger }) {
  return (
    <nav className="child-filter" aria-label="Filter points by child">
      <span>Show points for</span>
      <div>
        <Link
          to={pointsHref(null)}
          aria-current={ledger.selectedChildId === null ? "page" : undefined}
        >
          All children
        </Link>
        {ledger.children.map((child) => (
          <Link
            key={child.id}
            to={pointsHref(child.id)}
            aria-current={
              ledger.selectedChildId === child.id ? "page" : undefined
            }
          >
            {child.displayName}
            {child.isActive ? null : " (inactive)"}{" "}
            <span className="child-filter__balance">
              {formatBalance(child.balance)}{" "}
              <span className="visually-hidden">points</span>
            </span>
          </Link>
        ))}
      </div>
    </nav>
  );
}

function LedgerEntries({
  ledger,
  isAdult,
  viewerName,
}: {
  ledger: PointsLedger;
  isAdult: boolean;
  viewerName: string;
}) {
  const fetcher = useFetcher<PointsLedgerLoaderData>();
  const [older, setOlder] = useState<{
    entries: PointsLedgerEntry[];
    nextCursor: string | null;
  }>({ entries: [], nextCursor: ledger.nextCursor });
  const [appendedPage, setAppendedPage] = useState<PointsLedgerLoaderData>();
  // Fold each loaded page into the list once, while rendering, rather than in an effect.
  if (fetcher.data && fetcher.data !== appendedPage) {
    setAppendedPage(fetcher.data);
    if (fetcher.data.state === "ready") {
      setOlder({
        entries: [...older.entries, ...fetcher.data.ledger.entries],
        nextCursor: fetcher.data.ledger.nextCursor,
      });
    }
  }

  const selectedChild = ledger.children.find(
    (child) => child.id === ledger.selectedChildId,
  );
  const showsChildNames = isAdult && ledger.selectedChildId === null;
  const entries = [...ledger.entries, ...older.entries];
  const nextCursor = older.nextCursor;
  const loadingOlder = fetcher.state !== "idle";
  const olderError =
    !loadingOlder && fetcher.data?.state === "error"
      ? fetcher.data.error
      : null;
  const heading = !isAdult
    ? `How ${viewerName} earned them`
    : selectedChild
      ? `${selectedChild.displayName}’s points`
      : "Every child’s points";

  return (
    <>
      <div className="board__heading">
        <div>
          <p className="eyebrow">Newest first</p>
          <h2 id="ledger-heading">{heading}</h2>
        </div>
      </div>

      {entries.length === 0 ? (
        <p className="board__empty">
          {!isAdult
            ? "No points earned yet. Complete and approve a job, or show a good behaviour, to start the list."
            : selectedChild
              ? `${selectedChild.displayName} hasn’t earned any points yet.`
              : "No points have been earned yet."}
        </p>
      ) : (
        <ol className="earning-list" aria-labelledby="ledger-heading">
          {entries.map((entry) => (
            <li key={entry.id}>
              <span
                className={
                  entry.points < 0
                    ? "earning-list__points earning-list__points--negative"
                    : "earning-list__points"
                }
              >
                {formatPoints(entry.points)}
              </span>
              <span className="earning-list__detail">
                <strong>{entry.name}</strong>
                {showsChildNames ? (
                  <span className="earning-list__child">
                    {entry.childDisplayName}
                  </span>
                ) : null}
                <time dateTime={entry.awardedAtUtc}>
                  {formatAwardTime(entry.awardedAtUtc)}
                </time>
              </span>
              <span className="earning-list__balance">
                Balance {formatBalance(entry.balanceAfter)}
              </span>
            </li>
          ))}
        </ol>
      )}

      {olderError ? (
        <p className="error-message points-ledger__error" role="alert">
          {olderError}
        </p>
      ) : null}
      {nextCursor ? (
        <button
          type="button"
          className="points-ledger__more"
          disabled={loadingOlder}
          onClick={() =>
            fetcher.load(pointsHref(ledger.selectedChildId, nextCursor))
          }
        >
          {loadingOlder ? "Loading older entries…" : "Show older entries"}
        </button>
      ) : null}
    </>
  );
}
