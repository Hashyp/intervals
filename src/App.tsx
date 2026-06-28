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
  type IntervalId,
  type TrainingMode,
} from "./music/intervals";
import { Visualizer, type VisualizerState } from "./components/Visualizer";
import { RecentStrip } from "./components/RecentStrip";

type GuessState = {
  selectedId: IntervalId;
  correct: boolean;
};

type Score = {
  attempts: number;
  correct: number;
  streak: number;
  bestStreak: number;
  recent: boolean[];
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
  { id: "ascending", label: "Ascending" },
  { id: "descending", label: "Descending" },
  { id: "harmonic", label: "Together" },
  { id: "mixed", label: "Mixed" },
];

const INSTRUMENTS: readonly { id: InstrumentId; label: string }[] = [
  { id: "guitar", label: "Guitar" },
  { id: "piano", label: "Piano" },
];

const RECENT_MAX = 32;

const initialScore: Score = {
  attempts: 0,
  correct: 0,
  streak: 0,
  bestStreak: 0,
  recent: [],
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

  const playQuestion = useCallback(() => {
    if (audioIsBusy) {
      return;
    }
    void play(question, instrument);
  }, [audioIsBusy, instrument, play, question]);

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
        const recent = [...current.recent, correct].slice(-RECENT_MAX);

        return {
          attempts: current.attempts + 1,
          correct: current.correct + (correct ? 1 : 0),
          streak,
          bestStreak: Math.max(current.bestStreak, streak),
          recent,
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

  // keyboard shortcuts: Space = play, N = next
  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null;

      if (
        target &&
        (target.tagName === "BUTTON" ||
          target.tagName === "INPUT" ||
          target.tagName === "TEXTAREA" ||
          target.isContentEditable)
      ) {
        return;
      }

      if (event.code === "Space") {
        event.preventDefault();
        playQuestion();
      } else if (event.key === "n" || event.key === "N") {
        event.preventDefault();
        nextQuestion();
      }
    }

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [nextQuestion, playQuestion]);

  const feedbackText = getFeedbackText(
    guess,
    question,
    instrument,
    audioState,
  );
  const visualizerState: VisualizerState = guess
    ? "revealed"
    : audioState === "playing"
      ? "playing"
      : "idle";
  const feedbackClass = getFeedbackClass(guess, audioState);
  const activeCount = enabledIntervalIds.length;

  return (
    <>
      <a className="skip-link" href="#trainer">
        Skip to Trainer
      </a>
      <div className="app-shell">
        <header className="app-header">
          <a className="brand" href="#trainer">
            <svg
              className="brand-mark"
              viewBox="0 0 32 32"
              fill="none"
              aria-hidden="true"
            >
              <line
                x1="11"
                y1="16"
                x2="21"
                y2="16"
                stroke="#a8772b"
                strokeWidth="1.6"
              />
              <circle cx="9" cy="16" r="4.4" fill="#d4a24c" />
              <circle cx="23" cy="16" r="4.4" fill="#ecbb6a" />
            </svg>
            <span className="brand-name">
              Inter<b>vals</b>
            </span>
          </a>
          <div className="stats-pill" aria-label="Current progress">
            <div className="stats-pill__item">
              <span className="stats-pill__label">Acc</span>
              <span className="stats-pill__value is-accent">{accuracy}%</span>
            </div>
            <span className="stats-pill__divider" />
            <div className="stats-pill__item">
              <span className="stats-pill__label">Streak</span>
              <span className="stats-pill__value">{score.streak}</span>
            </div>
          </div>
        </header>

        <main id="main-content">
          <section
            className="trainer"
            id="trainer"
            aria-labelledby="page-title"
            tabIndex={-1}
          >
            <div className="trainer-head">
              <div className="trainer-head__title">
                <p className="eyebrow">Ear Training</p>
                <h1 id="page-title">
                  Hear two notes.
                  <br />
                  Name the <em>interval</em>.
                </h1>
              </div>
              <div
                className="mode-chip"
                aria-label={`Current playback mode: ${formatMode(question.mode)}`}
              >
                {formatMode(question.mode)}
              </div>
            </div>

            <Visualizer question={question} state={visualizerState} />

            <div className="playback">
              <button
                className="btn btn--primary"
                type="button"
                onClick={playQuestion}
                disabled={audioIsBusy}
              >
                <PlayIcon />
                {audioState === "loading"
                  ? "Loading"
                  : audioState === "playing"
                    ? "Playing"
                    : `Play ${formatInstrument(instrument)}`}
                <span className="kbd" aria-hidden="true">
                  Space
                </span>
              </button>
              <button
                className="btn btn--ghost"
                type="button"
                onClick={nextQuestion}
                disabled={audioIsBusy}
              >
                Next
                <span className="kbd" aria-hidden="true">
                  N
                </span>
              </button>
              {guess && !guess.correct ? (
                <button
                  className="btn--subtle"
                  type="button"
                  onClick={() => void playBetweenNotes(question, instrument)}
                  disabled={audioIsBusy}
                >
                  Play all notes between
                </button>
              ) : null}
            </div>

            <p
              aria-live="polite"
              className={`feedback ${feedbackClass}`}
              role="status"
            >
              {feedbackText}
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
                    className={`answer ${answerClass}`}
                    key={interval.id}
                    type="button"
                    onClick={() => handleGuess(interval.id)}
                    aria-disabled={guess ? "true" : undefined}
                  >
                    <span className="answer__code">{interval.shortName}</span>
                    <span className="answer__name">{interval.name}</span>
                  </button>
                );
              })}
            </div>
          </section>

          <section className="deck" aria-label="Practice settings and progress">
            <div>
              <div className="panel">
                <div className="panel__head">
                  <p className="eyebrow">Playback</p>
                  <h2>How notes are played</h2>
                </div>
                <div
                  className="segmented"
                  role="group"
                  aria-label="Playback direction"
                >
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
                <div
                  className="segmented"
                  style={{ marginTop: 12 }}
                  role="group"
                  aria-label="Instrument"
                >
                  {INSTRUMENTS.map((option) => (
                    <button
                      aria-pressed={instrument === option.id}
                      className={instrument === option.id ? "is-active" : ""}
                      key={option.id}
                      type="button"
                      onClick={() => setInstrument(option.id)}
                      disabled={audioIsBusy}
                      style={{
                        gridColumn:
                          INSTRUMENTS.length === 2 ? "span 2" : undefined,
                      }}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              </div>

              <div className="panel">
                <div className="panel__head">
                  <p className="eyebrow">Interval set</p>
                  <h2>
                    Focus the round{" "}
                    <span style={{ color: "var(--muted)", fontSize: "0.9rem" }}>
                      ({activeCount} active)
                    </span>
                  </h2>
                </div>
                <p className="sr-only" id="interval-lock-note">
                  Keep at least one interval active.
                </p>
                <div className="toggles">
                  {INTERVALS.map((interval) => {
                    const enabled = enabledIntervalIds.includes(interval.id);
                    const locked = enabled && enabledIntervalIds.length === 1;

                    return (
                      <button
                        aria-pressed={enabled}
                        aria-disabled={locked ? "true" : undefined}
                        aria-describedby={
                          locked ? "interval-lock-note" : undefined
                        }
                        className={`toggle ${enabled ? "is-active" : ""}`}
                        key={interval.id}
                        type="button"
                        onClick={() => toggleInterval(interval.id)}
                        disabled={audioIsBusy}
                      >
                        <span className="toggle__code">
                          {interval.shortName}
                        </span>
                        <span className="toggle__hint">
                          {locked ? "Only active" : interval.hint}
                        </span>
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>

            <div className="panel">
              <div className="panel__head">
                <p className="eyebrow">Progress</p>
                <h2>Today</h2>
              </div>
              <RecentStrip recent={score.recent} />
              <dl className="stat-grid">
                <div className="stat">
                  <dt className="stat__label">Accuracy</dt>
                  <dd className="stat__value is-accent">{accuracy}%</dd>
                </div>
                <div className="stat">
                  <dt className="stat__label">Correct</dt>
                  <dd className="stat__value">
                    {score.correct}
                    <span style={{ color: "var(--muted)", fontSize: "1.1rem" }}>
                      {" "}
                      / {score.attempts}
                    </span>
                  </dd>
                </div>
                <div className="stat">
                  <dt className="stat__label">Streak</dt>
                  <dd className="stat__value">{score.streak}</dd>
                </div>
                <div className="stat">
                  <dt className="stat__label">Best</dt>
                  <dd className="stat__value">{score.bestStreak}</dd>
                </div>
              </dl>
              <button
                className="btn btn--ghost full-width"
                style={{ marginTop: 16 }}
                type="button"
                onClick={resetScore}
                disabled={!canResetScore}
              >
                Reset score
              </button>
              {resetSnapshot ? (
                <div className="reset-undo">
                  <span role="status" aria-live="polite">
                    Score reset.
                  </span>
                  <button
                    className="text-btn"
                    type="button"
                    onClick={undoResetScore}
                  >
                    Undo
                  </button>
                </div>
              ) : null}
            </div>
          </section>
        </main>

        <p className="footnote">
          Listen · Name · Repeat — build a steadier ear
        </p>
      </div>
    </>
  );
}

function PlayIcon() {
  return (
    <svg
      className="btn__icon"
      viewBox="0 0 16 16"
      fill="currentColor"
      aria-hidden="true"
    >
      <path d="M3.5 2.2v11.6c0 .5.6.8 1 .5l8.4-5.8a.6.6 0 0 0 0-1L4.5 1.7a.6.6 0 0 0-1 .5z" />
    </svg>
  );
}

function getFeedbackText(
  guess: GuessState | null,
  question: ReturnType<typeof createQuestion>,
  instrument: InstrumentId,
  audioState: string,
) {
  if (audioState === "error") {
    return "Audio could not start. Click play again or check browser audio permissions.";
  }

  if (!guess) {
    return `Press play, listen to the ${instrument}, then choose the interval you hear.`;
  }

  if (guess.correct) {
    return (
      <>
        Correct — <strong>{question.interval.name}</strong>.{" "}
        {question.interval.hint}.
      </>
    );
  }

  return (
    <>
      Not yet. The answer was <strong>{question.interval.name}</strong>. Try
      “Play all notes between” to hear every half step.
    </>
  );
}

function getFeedbackClass(
  guess: GuessState | null,
  audioState: string,
): string {
  if (audioState === "error") {
    return "is-error";
  }
  if (!guess) {
    return "";
  }
  return guess.correct ? "is-correct" : "is-wrong";
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

  return "is-faded";
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
