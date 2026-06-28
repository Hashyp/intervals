import type { IntervalQuestion } from "../music/intervals";
import { midiToNoteName } from "../music/intervals";

export type VisualizerState = "idle" | "playing" | "revealed";

type Props = {
  question: IntervalQuestion;
  state: VisualizerState;
};

const MIN_MIDI = 59;
const MAX_MIDI = 73;

function position(midi: number) {
  const pct = ((midi - MIN_MIDI) / (MAX_MIDI - MIN_MIDI)) * 100;

  return Math.min(96, Math.max(4, pct));
}

export function Visualizer({ question, state }: Props) {
  if (state === "revealed") {
    const rootPct = position(question.rootMidi);
    const targetPct = position(question.targetMidi);
    const left = Math.min(rootPct, targetPct);
    const right = Math.max(rootPct, targetPct);

    return (
      <div className="visualizer" aria-hidden="true">
        <div className="staff" />
        <div
          className="interval-span"
          style={{ left: `${left}%`, width: `${Math.max(2, right - left)}%` }}
        >
          <span className="interval-span__label">
            {question.interval.shortName}
          </span>
        </div>
        <NoteDot
          pct={rootPct}
          label={midiToNoteName(question.rootMidi)}
          variant="root"
        />
        <NoteDot
          pct={targetPct}
          label={midiToNoteName(question.targetMidi)}
          variant="target"
        />
      </div>
    );
  }

  const playing = state === "playing";

  return (
    <div className={`visualizer ${playing ? "visualizer--playing" : ""}`}>
      <div className="eq" aria-hidden="true">
        {Array.from({ length: 11 }).map((_, index) => (
          <span key={index} style={{ animationDelay: `${index * 0.07}s` }} />
        ))}
      </div>
      <p className="visualizer-hint">
        {playing ? "Listening" : "Press play to listen"}
      </p>
    </div>
  );
}

function NoteDot({
  pct,
  label,
  variant,
}: {
  pct: number;
  label: string;
  variant: "root" | "target";
}) {
  return (
    <div
      className={`note-dot note-dot--${variant}`}
      style={{ left: `${pct}%` }}
    >
      <span className="note-dot__label">{label}</span>
    </div>
  );
}
