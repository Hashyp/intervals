import { useCallback, useEffect, useMemo, useState } from "react";
import { useIntervalAudio } from "./audio/useIntervalAudio";
import {
  DEFAULT_INTERVAL_IDS,
  INTERVALS,
  createQuestion,
  midiToNoteName,
  type IntervalId,
  type TrainingMode,
} from "./music/intervals";

type GuessState = {
  selectedId: IntervalId;
  correct: boolean;
};

type Score = {
  attempts: number;
  correct: number;
  streak: number;
  bestStreak: number;
};

const MODES: readonly { id: TrainingMode; label: string }[] = [
  {
    id: "ascending",
    label: "Ascending",
  },
  {
    id: "descending",
    label: "Descending",
  },
  {
    id: "harmonic",
    label: "Together",
  },
  {
    id: "mixed",
    label: "Mixed",
  },
];

const initialScore: Score = {
  attempts: 0,
  correct: 0,
  streak: 0,
  bestStreak: 0,
};

export function App() {
  const [enabledIntervalIds, setEnabledIntervalIds] = useState<IntervalId[]>([
    ...DEFAULT_INTERVAL_IDS,
  ]);
  const [mode, setMode] = useState<TrainingMode>("ascending");
  const [question, setQuestion] = useState(() =>
    createQuestion({
      enabledIntervalIds: DEFAULT_INTERVAL_IDS,
      mode: "ascending",
    }),
  );
  const [guess, setGuess] = useState<GuessState | null>(null);
  const [score, setScore] = useState<Score>(initialScore);
  const { audioState, play } = useIntervalAudio();

  const accuracy = useMemo(() => {
    if (score.attempts === 0) {
      return 0;
    }

    return Math.round((score.correct / score.attempts) * 100);
  }, [score.attempts, score.correct]);

  const nextQuestion = useCallback(() => {
    setQuestion(
      createQuestion({
        enabledIntervalIds,
        mode,
      }),
    );
    setGuess(null);
  }, [enabledIntervalIds, mode]);

  useEffect(() => {
    nextQuestion();
  }, [nextQuestion]);

  const handleGuess = useCallback(
    (selectedId: IntervalId) => {
      if (guess) {
        return;
      }

      const correct = selectedId === question.interval.id;

      setGuess({
        selectedId,
        correct,
      });
      setScore((current) => {
        const streak = correct ? current.streak + 1 : 0;

        return {
          attempts: current.attempts + 1,
          correct: current.correct + (correct ? 1 : 0),
          streak,
          bestStreak: Math.max(current.bestStreak, streak),
        };
      });
    },
    [guess, question.interval.id],
  );

  const toggleInterval = useCallback((intervalId: IntervalId) => {
    setEnabledIntervalIds((current) => {
      const isEnabled = current.includes(intervalId);

      if (isEnabled && current.length === 1) {
        return current;
      }

      return isEnabled
        ? current.filter((id) => id !== intervalId)
        : [...current, intervalId];
    });
  }, []);

  const resetScore = useCallback(() => {
    setScore(initialScore);
  }, []);

  const feedbackText = getFeedbackText(guess, question);
  const rootNote = midiToNoteName(question.rootMidi);
  const targetNote = midiToNoteName(question.targetMidi);

  return (
    <main className="app-shell">
      <section className="intro-band" aria-labelledby="page-title">
        <div className="intro-copy">
          <p className="eyebrow">Ear training</p>
          <h1 id="page-title">Hear two notes. Name the interval.</h1>
          <p className="lede">
            Build a steadier ear with short rounds, clear feedback, and focused
            interval sets.
          </p>
        </div>
        <img
          className="studio-image"
          src="https://images.unsplash.com/photo-1511379938547-c1f69419868d?auto=format&fit=crop&w=1200&q=80"
          alt="Piano keys and studio equipment"
        />
      </section>

      <section className="trainer" aria-label="Interval trainer">
        <div className="trainer-header">
          <div>
            <p className="eyebrow">Current round</p>
            <h2>{guess ? `${rootNote} to ${targetNote}` : "Listen first"}</h2>
          </div>
          <div className="mode-chip">{formatMode(question.mode)}</div>
        </div>

        <div className="playback-row">
          <button
            className="primary-button"
            type="button"
            onClick={() => void play(question)}
            disabled={audioState === "playing" || audioState === "loading"}
          >
            {audioState === "loading"
              ? "Loading guitar"
              : audioState === "playing"
                ? "Playing"
                : "Play guitar notes"}
          </button>
          <button className="secondary-button" type="button" onClick={nextQuestion}>
            Next interval
          </button>
        </div>

        <p className={`feedback ${guess?.correct ? "is-correct" : ""}`}>
          {audioState === "error"
            ? "Audio could not start. Click play again or check browser audio permissions."
            : feedbackText}
        </p>

        <div className="answer-grid" aria-label="Interval answers">
          {INTERVALS.map((interval) => {
            const answerClass = getAnswerClass(interval.id, question.interval.id, guess);

            return (
              <button
                className={`answer-button ${answerClass}`}
                key={interval.id}
                type="button"
                onClick={() => handleGuess(interval.id)}
                disabled={Boolean(guess)}
              >
                <span>{interval.shortName}</span>
                <strong>{interval.name}</strong>
              </button>
            );
          })}
        </div>
      </section>

      <section className="practice-layout" aria-label="Practice settings and score">
        <div className="settings-panel">
          <div className="section-heading">
            <p className="eyebrow">Practice mode</p>
            <h2>Choose how notes are played</h2>
          </div>
          <div className="segmented-control">
            {MODES.map((modeOption) => (
              <button
                aria-pressed={mode === modeOption.id}
                className={mode === modeOption.id ? "is-active" : ""}
                key={modeOption.id}
                type="button"
                onClick={() => setMode(modeOption.id)}
              >
                {modeOption.label}
              </button>
            ))}
          </div>
        </div>

        <div className="settings-panel">
          <div className="section-heading">
            <p className="eyebrow">Interval set</p>
            <h2>Focus the round</h2>
          </div>
          <div className="interval-toggle-grid">
            {INTERVALS.map((interval) => {
              const enabled = enabledIntervalIds.includes(interval.id);
              const locked = enabled && enabledIntervalIds.length === 1;

              return (
                <button
                  aria-pressed={enabled}
                  className={enabled ? "is-active" : ""}
                  disabled={locked}
                  key={interval.id}
                  type="button"
                  onClick={() => toggleInterval(interval.id)}
                  title={locked ? "Keep at least one interval active" : interval.hint}
                >
                  <span>{interval.shortName}</span>
                  <small>{interval.hint}</small>
                </button>
              );
            })}
          </div>
        </div>

        <div className="score-panel">
          <div className="section-heading">
            <p className="eyebrow">Progress</p>
            <h2>Today</h2>
          </div>
          <dl className="score-grid">
            <div>
              <dt>Accuracy</dt>
              <dd>{accuracy}%</dd>
            </div>
            <div>
              <dt>Correct</dt>
              <dd>
                {score.correct}/{score.attempts}
              </dd>
            </div>
            <div>
              <dt>Streak</dt>
              <dd>{score.streak}</dd>
            </div>
            <div>
              <dt>Best</dt>
              <dd>{score.bestStreak}</dd>
            </div>
          </dl>
          <button className="secondary-button full-width" type="button" onClick={resetScore}>
            Reset score
          </button>
        </div>
      </section>
    </main>
  );
}

function getFeedbackText(
  guess: GuessState | null,
  question: ReturnType<typeof createQuestion>,
) {
  if (!guess) {
    return "Play the guitar notes, then choose the interval you hear.";
  }

  if (guess.correct) {
    return `Correct. ${question.interval.name} is ${question.interval.hint}.`;
  }

  return `Not yet. The answer was ${question.interval.name}, ${question.interval.hint}.`;
}

function getAnswerClass(
  intervalId: IntervalId,
  correctId: IntervalId,
  guess: GuessState | null,
) {
  if (!guess) {
    return "";
  }

  if (intervalId === correctId) {
    return "is-correct";
  }

  if (intervalId === guess.selectedId) {
    return "is-wrong";
  }

  return "is-muted";
}

function formatMode(mode: string) {
  if (mode === "harmonic") {
    return "Together";
  }

  return mode[0].toUpperCase() + mode.slice(1);
}
