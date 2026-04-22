import { useCallback, useEffect, useMemo, useState } from "react";
import { useIntervalAudio, type InstrumentId } from "./audio/useIntervalAudio";
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

const INSTRUMENTS: readonly { id: InstrumentId; label: string }[] = [
  {
    id: "guitar",
    label: "Guitar",
  },
  {
    id: "piano",
    label: "Piano",
  },
];

const initialScore: Score = {
  attempts: 0,
  correct: 0,
  streak: 0,
  bestStreak: 0,
};

export function App() {
  const [enabledIntervalIds, setEnabledIntervalIds] = useState<IntervalId[]>(() =>
    getInitialIntervalIds(),
  );
  const [mode, setMode] = useState<TrainingMode>(() => getInitialMode());
  const [instrument, setInstrument] = useState<InstrumentId>(() => getInitialInstrument());
  const [question, setQuestion] = useState(() =>
    createQuestion({
      enabledIntervalIds: getInitialIntervalIds(),
      mode: getInitialMode(),
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

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);

    params.set("mode", mode);
    params.set("intervals", serializeIntervalIds(enabledIntervalIds));
    params.set("instrument", instrument);

    const nextUrl = `${window.location.pathname}?${params.toString()}`;
    window.history.replaceState({}, "", nextUrl);
  }, [enabledIntervalIds, instrument, mode]);

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

  const feedbackText = getFeedbackText(guess, question, instrument);
  const rootNote = midiToNoteName(question.rootMidi);
  const targetNote = midiToNoteName(question.targetMidi);

  return (
    <>
      <a className="skip-link" href="#main-content">
        Skip to Trainer
      </a>
      <main className="app-shell" id="main-content">
      <section className="intro-band" aria-labelledby="page-title">
        <div className="intro-copy">
          <p className="eyebrow">Ear training</p>
          <h1 id="page-title">Hear Two Notes. Name the Interval.</h1>
          <p className="lede">
            Build a steadier ear with short rounds, clear feedback, and focused
            interval sets.
          </p>
        </div>
        <img
          className="studio-image"
          src="https://images.unsplash.com/photo-1510915361894-db8b60106cb1?auto=format&fit=crop&w=1200&q=80"
          alt="Live instrument setup under warm stage light"
          width="1200"
          height="900"
          fetchPriority="high"
        />
      </section>

      <section className="trainer" aria-label="Interval trainer">
        <div className="trainer-header">
          <div>
            <p className="eyebrow">Current round</p>
            <h2>{guess ? `${rootNote} to ${targetNote}` : "Listen First"}</h2>
          </div>
          <div className="mode-chip">{formatMode(question.mode)}</div>
        </div>

        <div className="playback-row">
          <button
            className="primary-button"
            type="button"
            onClick={() => void play(question, instrument)}
            disabled={audioState === "playing" || audioState === "loading"}
          >
            {audioState === "loading"
              ? "Loading…"
              : audioState === "playing"
                ? "Playing"
                : `Play ${formatInstrument(instrument)} Notes`}
          </button>
          <button className="secondary-button" type="button" onClick={nextQuestion}>
            Next Interval
          </button>
        </div>

        <p
          aria-live="polite"
          className={`feedback ${guess?.correct ? "is-correct" : ""}`}
          role="status"
        >
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
            <h2>Choose How Notes Are Played</h2>
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
            <p className="eyebrow">Instrument</p>
            <h2>Choose Sample Bank</h2>
          </div>
          <div className="segmented-control">
            {INSTRUMENTS.map((option) => (
              <button
                aria-pressed={instrument === option.id}
                className={instrument === option.id ? "is-active" : ""}
                key={option.id}
                type="button"
                onClick={() => setInstrument(option.id)}
              >
                {option.label}
              </button>
            ))}
          </div>
        </div>

        <div className="settings-panel">
          <div className="section-heading">
            <p className="eyebrow">Interval set</p>
            <h2>Focus the Round</h2>
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
            Reset Score
          </button>
        </div>
      </section>
      </main>
    </>
  );
}

function getFeedbackText(
  guess: GuessState | null,
  question: ReturnType<typeof createQuestion>,
  instrument: InstrumentId,
) {
  if (!guess) {
    return `Play the ${instrument} notes, then choose the interval you hear.`;
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

function formatInstrument(instrument: InstrumentId) {
  return instrument[0].toUpperCase() + instrument.slice(1);
}

function getInitialMode(): TrainingMode {
  const params = new URLSearchParams(window.location.search);
  const mode = params.get("mode");

  if (mode === "ascending" || mode === "descending" || mode === "harmonic" || mode === "mixed") {
    return mode;
  }

  return "ascending";
}

function getInitialInstrument(): InstrumentId {
  const params = new URLSearchParams(window.location.search);
  const instrument = params.get("instrument");

  if (instrument === "guitar" || instrument === "piano") {
    return instrument;
  }

  return "guitar";
}

function getInitialIntervalIds(): IntervalId[] {
  const params = new URLSearchParams(window.location.search);
  const raw = params.get("intervals");

  if (!raw) {
    return [...DEFAULT_INTERVAL_IDS];
  }

  const validIds = INTERVALS.map((interval) => interval.id);
  const requestedIds = raw
    .split(",")
    .map((value) => value.trim())
    .filter((value): value is IntervalId => validIds.includes(value as IntervalId));

  if (requestedIds.length === 0) {
    return [...DEFAULT_INTERVAL_IDS];
  }

  return INTERVALS.filter((interval) => requestedIds.includes(interval.id)).map(
    (interval) => interval.id,
  );
}

function serializeIntervalIds(intervalIds: readonly IntervalId[]) {
  return INTERVALS.filter((interval) => intervalIds.includes(interval.id))
    .map((interval) => interval.id)
    .join(",");
}
