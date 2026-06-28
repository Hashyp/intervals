import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
  INTERVALS,
  type IntervalDefinition,
  type IntervalQuestion,
} from "../music/intervals";
import { Visualizer, type VisualizerState } from "./Visualizer";

const majorThird = getInterval("M3");
const perfectFifth = getInterval("P5");

function getInterval(id: IntervalDefinition["id"]) {
  const interval = INTERVALS.find((candidate) => candidate.id === id);

  if (!interval) {
    throw new Error(`Missing interval fixture: ${id}`);
  }

  return interval;
}

function makeQuestion(
  overrides: Partial<IntervalQuestion> = {},
): IntervalQuestion {
  return {
    id: "question-1",
    interval: majorThird,
    rootMidi: 60,
    targetMidi: 64,
    mode: "ascending",
    ...overrides,
  };
}

function renderVisualizer(
  state: VisualizerState,
  overrides: Partial<IntervalQuestion> = {},
) {
  return render(
    <Visualizer question={makeQuestion(overrides)} state={state} />,
  );
}

describe("Visualizer", () => {
  it("shows the idle prompt before playback starts", () => {
    const { container } = renderVisualizer("idle");

    expect(screen.getByText("Press play to listen")).toBeInTheDocument();
    expect(container.firstElementChild).toHaveClass("visualizer");
    expect(container.firstElementChild).not.toHaveClass("visualizer--playing");
  });

  it("shows listening copy and playing state during playback", () => {
    const { container } = renderVisualizer("playing");

    expect(screen.getByText("Listening")).toBeInTheDocument();
    expect(container.firstElementChild).toHaveClass(
      "visualizer",
      "visualizer--playing",
    );
  });

  it.each([
    {
      name: "ascending",
      question: {
        interval: majorThird,
        rootMidi: 60,
        targetMidi: 64,
        mode: "ascending",
      },
      labels: ["M3", "C4", "E4"],
    },
    {
      name: "descending",
      question: {
        interval: perfectFifth,
        rootMidi: 67,
        targetMidi: 60,
        mode: "descending",
      },
      labels: ["P5", "G4", "C4"],
    },
  ] satisfies readonly {
    name: string;
    question: Partial<IntervalQuestion>;
    labels: readonly string[];
  }[])(
    "reveals interval and note labels for $name questions",
    ({ question, labels }) => {
      renderVisualizer("revealed", question);

      for (const label of labels) {
        expect(screen.getByText(label)).toBeInTheDocument();
      }
    },
  );

  it("clamps out-of-range note positions inside the visualizer", () => {
    const { container } = renderVisualizer("revealed", {
      rootMidi: 40,
      targetMidi: 90,
    });
    const noteDots = container.querySelectorAll<HTMLElement>(".note-dot");
    const intervalSpan = container.querySelector<HTMLElement>(".interval-span");

    expect(noteDots).toHaveLength(2);
    expect(noteDots[0]).toHaveStyle("left: 4%");
    expect(noteDots[1]).toHaveStyle("left: 96%");
    expect(intervalSpan).toHaveStyle("left: 4%; width: 92%");
  });
});
