type Props = {
  recent: readonly boolean[];
};

const MAX_SHOWN = 18;

export function RecentStrip({ recent }: Props) {
  const shown = recent.slice(-MAX_SHOWN);
  const newestIndex = shown.length - 1;

  if (shown.length === 0) {
    return (
      <div className="recent" aria-label="Recent attempts">
        <span className="recent__empty">No attempts yet — play to begin</span>
      </div>
    );
  }

  return (
    <div className="recent" aria-label="Recent attempts">
      {shown.map((hit, index) => (
        <span
          key={`${shown.length}-${index}`}
          className={
            hit
              ? `recent__dot recent__dot--hit${
                  index === newestIndex ? " recent__dot--new" : ""
                }`
              : `recent__dot recent__dot--miss${
                  index === newestIndex ? " recent__dot--new" : ""
                }`
          }
        />
      ))}
    </div>
  );
}
