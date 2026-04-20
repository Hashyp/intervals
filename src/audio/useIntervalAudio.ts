import { useCallback, useEffect, useRef, useState } from "react";
import * as Tone from "tone";
import type { IntervalQuestion } from "../music/intervals";
import { midiToFrequency } from "../music/intervals";

type AudioState = "idle" | "playing" | "error";

export function useIntervalAudio() {
  const synthRef = useRef<Tone.PolySynth | null>(null);
  const [audioState, setAudioState] = useState<AudioState>("idle");

  const getSynth = useCallback(() => {
    if (!synthRef.current) {
      synthRef.current = new Tone.PolySynth(Tone.Synth, {
        oscillator: {
          type: "triangle",
        },
        envelope: {
          attack: 0.025,
          decay: 0.15,
          sustain: 0.65,
          release: 0.28,
        },
        volume: -9,
      }).toDestination();
    }

    return synthRef.current;
  }, []);

  const play = useCallback(
    async (question: IntervalQuestion) => {
      try {
        setAudioState("playing");
        await Tone.start();

        const synth = getSynth();
        const now = Tone.now() + 0.08;
        const rootFrequency = midiToFrequency(question.rootMidi);
        const targetFrequency = midiToFrequency(question.targetMidi);

        synth.releaseAll();

        if (question.mode === "harmonic") {
          synth.triggerAttackRelease([rootFrequency, targetFrequency], 1.25, now);
        } else {
          synth.triggerAttackRelease(rootFrequency, 0.65, now);
          synth.triggerAttackRelease(targetFrequency, 0.75, now + 0.9);
        }

        window.setTimeout(() => {
          setAudioState("idle");
        }, question.mode === "harmonic" ? 1350 : 1750);
      } catch {
        setAudioState("error");
      }
    },
    [getSynth],
  );

  useEffect(() => {
    return () => {
      synthRef.current?.dispose();
      synthRef.current = null;
    };
  }, []);

  return {
    audioState,
    play,
  };
}

