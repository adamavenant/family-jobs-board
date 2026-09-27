import type { WhoseTurn } from "../../api/today";

export function WhoseTurnCard({
  whoseTurn,
  date,
  isToday,
}: {
  whoseTurn: WhoseTurn;
  date: string;
  isToday: boolean;
}) {
  return (
    <div className="whose-turn-card" role="status">
      <span className="whose-turn-card__question">
        {phraseForDate(whoseTurn.question, date, isToday)}
      </span>
      <span className="whose-turn-card__child">
        <span className="whose-turn-card__badge" aria-hidden="true">
          {whoseTurn.childDisplayName.charAt(0).toUpperCase()}
        </span>
        {whoseTurn.childDisplayName}
      </span>
    </div>
  );
}

function phraseForDate(
  question: string,
  date: string,
  isToday: boolean,
): string {
  const trimmed = question.trim();
  if (isToday) {
    return trimmed;
  }

  const weekday = new Intl.DateTimeFormat("en", { weekday: "long" }).format(
    new Date(`${date}T12:00:00`),
  );
  if (/today\?$/i.test(trimmed)) {
    return trimmed.replace(/today\?$/i, `on ${weekday}?`);
  }
  if (/today$/i.test(trimmed)) {
    return trimmed.replace(/today$/i, `on ${weekday}`);
  }

  return `${trimmed} (${weekday})`;
}
