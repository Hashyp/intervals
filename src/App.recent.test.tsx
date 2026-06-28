import {
  cleanup,
  fireEvent,
  render,
  screen,
  within,
} from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";

const audioMocks = vi.hoisted(() => ({
  play: vi.fn(),
  playBetweenNotes: vi.fn(),
  stop: vi.fn(),
}));

const recentStripCalls = vi.hoisted(() => [] as boolean[][]);

vi.mock("./audio/useIntervalAudio", () => ({
  useIntervalAudio: () => ({
    audioState: "idle",
    play: audioMocks.play,
    playBetweenNotes: audioMocks.playBetweenNotes,
    stop: audioMocks.stop,
  }),
}));

vi.mock("./components/RecentStrip", async () => {
  const { createElement } =
    await vi.importActual<typeof import("react")>("react");

  return {
    RecentStrip: ({ recent }: { recent: readonly boolean[] }) => {
      recentStripCalls.push([...recent]);

      return createElement(
        "div",
        {
          "aria-label": "Recent attempts",
          "data-testid": "recent-strip",
        },
        recent.map((hit, index) =>
          createElement("span", {
            "data-hit": hit ? "hit" : "miss",
            "data-testid": "app-recent-dot",
            key: index,
          }),
        ),
      );
    },
  };
});

describe("App recent score history", () => {
  beforeEach(() => {
    window.history.pushState(
      {},
      "",
      "/?intervals=m2&mode=ascending&instrument=guitar",
    );
    recentStripCalls.length = 0;
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it("records correct and wrong guesses as hit and miss history", () => {
    render(<App />);

    chooseCorrectAnswer();
    expect(lastRecent()).toEqual([true]);
    expectCorrectStat("1 / 1");

    nextQuestion();
    chooseWrongAnswer();

    expect(lastRecent()).toEqual([true, false]);
    expectCorrectStat("1 / 2");
  });

  it("keeps only the 32 most recent attempts", () => {
    render(<App />);

    const correctAnswer = screen.getByRole("button", { name: /Minor second/i });
    const wrongAnswer = screen.getByRole("button", { name: /Major second/i });
    const nextButton = screen.getByRole("button", { name: "Next" });

    const expectedRecent = Array.from({ length: 33 }, (_, index) => {
      const correct = index % 2 === 0;
      fireEvent.click(correct ? correctAnswer : wrongAnswer);

      if (index < 32) {
        fireEvent.click(nextButton);
      }

      return correct;
    }).slice(-32);

    expect(lastRecent()).toEqual(expectedRecent);
  });

  it("clears recent history on reset and restores it on undo", () => {
    render(<App />);

    chooseCorrectAnswer();
    nextQuestion();
    chooseWrongAnswer();
    expect(lastRecent()).toEqual([true, false]);

    fireEvent.click(screen.getByRole("button", { name: "Reset score" }));

    expect(lastRecent()).toEqual([]);
    expectCorrectStat("0 / 0");
    expect(screen.getByText("Score reset.")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Undo" }));

    expect(lastRecent()).toEqual([true, false]);
    expectCorrectStat("1 / 2");
  });
});

function chooseCorrectAnswer() {
  fireEvent.click(screen.getByRole("button", { name: /Minor second/i }));
}

function chooseWrongAnswer() {
  fireEvent.click(screen.getByRole("button", { name: /Major second/i }));
}

function nextQuestion() {
  fireEvent.click(screen.getByRole("button", { name: "Next" }));
}

function lastRecent() {
  const recent = recentStripCalls.at(-1);

  if (!recent) {
    throw new Error("RecentStrip was not rendered");
  }

  return recent;
}

function expectCorrectStat(expectedText: string) {
  const stat = screen.getByText("Correct").closest(".stat");

  if (!stat) {
    throw new Error("Correct stat was not rendered");
  }

  expect(
    within(stat as HTMLElement).getByText((_, element) => {
      return (
        element?.tagName.toLowerCase() === "dd" &&
        normalizeText(element.textContent) === expectedText
      );
    }),
  ).toBeInTheDocument();
}

function normalizeText(text: string | null) {
  return text?.replace(/\s+/g, " ").trim() ?? "";
}
