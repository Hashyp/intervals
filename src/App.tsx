import {
  useCallback,
  useEffect,
  useMemo,
  useReducer,
  useRef,
  useState,
} from "react";
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

type TrainingState = {
  enabledIntervalIds: IntervalId[];
  mode: TrainingMode;
  question: ReturnType<typeof createQuestion>;
  guess: GuessState | null;
};

type TrainingAction =
  | { type: "next" }
  | { type: "setMode"; mode: TrainingMode }
  | { type: "toggleInterval"; intervalId: IntervalId }
  | { type: "guess"; selectedId: IntervalId };

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
  const [training, dispatchTraining] = useReducer(
    trainingReducer,
    undefined,
    createInitialTrainingState,
  );
  const [instrument, setInstrument] = useState<InstrumentId>(() =>
    getInitialInstrument(),
  );
  const [score, setScore] = useState<Score>(initialScore);
  const [resetSnapshot, setResetSnapshot] = useState<Score | null>(null);
  const guessLockedRef = useRef(false);
  const resetUndoTimeoutRef = useRef<number | null>(null);
  const { audioState, play, playBetweenNotes, stop } = useIntervalAudio();
  const { enabledIntervalIds, guess, mode, question } = training;
  const audioIsBusy = audioState === "playing" || audioState === "loading";

  const accuracy = useMemo(() => {
    if (score.attempts === 0) {
      return 0;
    }

    return Math.round((score.correct / score.attempts) * 100);
  }, [score.attempts, score.correct]);

  const canResetScore = useMemo(
    () => !scoresAreEqual(score, initialScore),
    [score],
  );

  const clearResetUndoTimeout = useCallback(() => {
    if (resetUndoTimeoutRef.current !== null) {
      window.clearTimeout(resetUndoTimeoutRef.current);
      resetUndoTimeoutRef.current = null;
    }
  }, []);

  const dismissResetUndo = useCallback(() => {
    clearResetUndoTimeout();
    setResetSnapshot(null);
  }, [clearResetUndoTimeout]);

  const resetQuestionFlow = useCallback(() => {
    stop();
    guessLockedRef.current = false;
  }, [stop]);

  const nextQuestion = useCallback(() => {
    resetQuestionFlow();
    dispatchTraining({ type: "next" });
  }, [resetQuestionFlow]);

  const changeMode = useCallback(
    (nextMode: TrainingMode) => {
      resetQuestionFlow();
      dispatchTraining({ type: "setMode", mode: nextMode });
    },
    [resetQuestionFlow],
  );

  const toggleInterval = useCallback(
    (intervalId: IntervalId) => {
      resetQuestionFlow();
      dispatchTraining({ type: "toggleInterval", intervalId });
    },
    [resetQuestionFlow],
  );

  useEffect(() => clearResetUndoTimeout, [clearResetUndoTimeout]);

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
      if (guessLockedRef.current || guess) {
        return;
      }

      const correct = selectedId === question.interval.id;

      guessLockedRef.current = true;
      dismissResetUndo();
      dispatchTraining({ type: "guess", selectedId });
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
    [dismissResetUndo, guess, question.interval.id],
  );

  const resetScore = useCallback(() => {
    if (!canResetScore) {
      return;
    }

    clearResetUndoTimeout();
    setResetSnapshot(score);
    setScore(initialScore);
    resetUndoTimeoutRef.current = window.setTimeout(() => {
      setResetSnapshot(null);
      resetUndoTimeoutRef.current = null;
    }, 6000);
  }, [canResetScore, clearResetUndoTimeout, score]);

  const undoResetScore = useCallback(() => {
    if (!resetSnapshot) {
      return;
    }

    clearResetUndoTimeout();
    setScore(resetSnapshot);
    setResetSnapshot(null);
  }, [clearResetUndoTimeout, resetSnapshot]);

  const feedbackText = getFeedbackText(guess, question, instrument);
  const rootNote = midiToNoteName(question.rootMidi);
  const targetNote = midiToNoteName(question.targetMidi);

  return (
    <>
      <a className="skip-link" href="#trainer">
        Skip to Trainer
      </a>
      <main className="app-shell" id="main-content">
        <section
          className="trainer"
          id="trainer"
          aria-labelledby="page-title"
          tabIndex={-1}
        >
          <div className="trainer-header">
            <div>
              <p className="eyebrow">Ear training</p>
              <h1 id="page-title">Hear Two Notes. Name the Interval.</h1>
              <p className="trainer-lede">
                Build a steadier ear with short rounds, clear feedback, and
                focused interval sets.
              </p>
            </div>
            <div
              className="mode-chip"
              aria-label={`Mode: ${formatMode(question.mode)}`}
            >
              {formatMode(question.mode)}
            </div>
          </div>

          <div className="round-heading">
            <p className="eyebrow">Current round</p>
            <h2>{guess ? `${rootNote} to ${targetNote}` : "Listen First"}</h2>
          </div>

          <div className="playback-row">
            <button
              className="primary-button"
              type="button"
              onClick={() => void play(question, instrument)}
              disabled={audioIsBusy}
            >
              {audioState === "loading"
                ? "Loading…"
                : audioState === "playing"
                  ? "Playing"
                  : `Play ${formatInstrument(instrument)} Notes`}
            </button>
            <button
              className="secondary-button"
              type="button"
              onClick={nextQuestion}
              disabled={audioIsBusy}
            >
              Next Interval
            </button>
            {guess && !guess.correct ? (
              <button
                className="secondary-button"
                type="button"
                onClick={() => void playBetweenNotes(question, instrument)}
                disabled={audioIsBusy}
              >
                Play All Notes Between
              </button>
            ) : null}
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
              const answerClass = getAnswerClass(
                interval.id,
                question.interval.id,
                guess,
              );

              return (
                <button
                  className={`answer-button ${answerClass}`}
                  key={interval.id}
                  type="button"
                  onClick={() => handleGuess(interval.id)}
                  aria-disabled={guess ? "true" : undefined}
                >
                  <span>{interval.shortName}</span>
                  <strong>{interval.name}</strong>
                </button>
              );
            })}
          </div>
        </section>

        <section className="intro-band" aria-labelledby="practice-focus-title">
          <div className="intro-copy">
            <p className="eyebrow">Practice focus</p>
            <h2 id="practice-focus-title">Short Rounds, Clear Feedback</h2>
            <p className="lede">
              Replay the notes, check the answer, and adjust the interval set as
              your ear gets steadier.
            </p>
          </div>
          <img
            className="studio-image"
            src="https://images.unsplash.com/photo-1510915361894-db8b60106cb1?auto=format&fit=crop&w=1200&q=80"
            alt="Live instrument setup under warm stage light"
            width="1200"
            height="900"
            loading="lazy"
          />
        </section>

        <section
          className="practice-layout"
          aria-label="Practice settings and score"
        >
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
                  onClick={() => changeMode(modeOption.id)}
                  disabled={audioIsBusy}
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
                  disabled={audioIsBusy}
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
            <p className="sr-only" id="interval-lock-note">
              Keep at least one interval active.
            </p>
            <div className="interval-toggle-grid">
              {INTERVALS.map((interval) => {
                const enabled = enabledIntervalIds.includes(interval.id);
                const locked = enabled && enabledIntervalIds.length === 1;

                return (
                  <button
                    aria-pressed={enabled}
                    aria-disabled={locked ? "true" : undefined}
                    aria-describedby={locked ? "interval-lock-note" : undefined}
                    className={enabled ? "is-active" : ""}
                    key={interval.id}
                    type="button"
                    onClick={() => toggleInterval(interval.id)}
                    disabled={audioIsBusy}
                  >
                    <span>{interval.shortName}</span>
                    <small>
                      {locked ? "Only active interval" : interval.hint}
                    </small>
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
            <button
              className="secondary-button full-width"
              type="button"
              onClick={resetScore}
              disabled={!canResetScore}
            >
              Reset Score
            </button>
            {resetSnapshot ? (
              <div className="reset-undo">
                <span role="status" aria-live="polite">
                  Score reset.
                </span>
                <button
                  className="text-button"
                  type="button"
                  onClick={undoResetScore}
                >
                  Undo Reset
                </button>
              </div>
            ) : null}
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

  return `Not yet. The answer was ${question.interval.name}, ${question.interval.hint}. Use "Play All Notes Between" to hear every half step.`;
}

function trainingReducer(
  state: TrainingState,
  action: TrainingAction,
): TrainingState {
  switch (action.type) {
    case "next":
      return {
        ...state,
        question: createTrainingQuestion(state.enabledIntervalIds, state.mode),
        guess: null,
      };

    case "setMode":
      return {
        ...state,
        mode: action.mode,
        question: createTrainingQuestion(state.enabledIntervalIds, action.mode),
        guess: null,
      };

    case "toggleInterval": {
      const enabledIntervalIds = getToggledIntervalIds(
        state.enabledIntervalIds,
        action.intervalId,
      );

      if (enabledIntervalIds === state.enabledIntervalIds) {
        return state;
      }

      return {
        ...state,
        enabledIntervalIds,
        question: createTrainingQuestion(enabledIntervalIds, state.mode),
        guess: null,
      };
    }

    case "guess":
      if (state.guess) {
        return state;
      }

      return {
        ...state,
        guess: {
          selectedId: action.selectedId,
          correct: action.selectedId === state.question.interval.id,
        },
      };
  }
}

function createInitialTrainingState(): TrainingState {
  const enabledIntervalIds = getInitialIntervalIds();
  const mode = getInitialMode();

  return {
    enabledIntervalIds,
    mode,
    question: createTrainingQuestion(enabledIntervalIds, mode),
    guess: null,
  };
}

function createTrainingQuestion(
  enabledIntervalIds: readonly IntervalId[],
  mode: TrainingMode,
) {
  return createQuestion({
    enabledIntervalIds,
    mode,
  });
}

function getToggledIntervalIds(current: IntervalId[], intervalId: IntervalId) {
  const isEnabled = current.includes(intervalId);

  if (isEnabled && current.length === 1) {
    return current;
  }

  return isEnabled
    ? current.filter((id) => id !== intervalId)
    : [...current, intervalId];
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

  if (
    mode === "ascending" ||
    mode === "descending" ||
    mode === "harmonic" ||
    mode === "mixed"
  ) {
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
    .filter((value): value is IntervalId =>
      validIds.includes(value as IntervalId),
    );

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

function scoresAreEqual(first: Score, second: Score) {
  return (
    first.attempts === second.attempts &&
    first.correct === second.correct &&
    first.streak === second.streak &&
    first.bestStreak === second.bestStreak
  );
}
