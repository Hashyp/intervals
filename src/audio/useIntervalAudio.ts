import { useCallback, useEffect, useRef, useState } from "react";
import * as Tone from "tone";
import type { IntervalQuestion } from "../music/intervals";
import { midiToNoteName } from "../music/intervals";

type AudioState = "idle" | "loading" | "playing" | "error";

const GUITAR_SAMPLE_URLS = {
  C4: "c4.mp3",
  Db4: "db4.mp3",
  D4: "d4.mp3",
  Eb4: "eb4.mp3",
  E4: "e4.mp3",
  F4: "f4.mp3",
  Gb4: "gb4.mp3",
  G4: "g4.mp3",
  Ab4: "ab4.mp3",
  A4: "a4.mp3",
  Bb4: "bb4.mp3",
  B4: "b4.mp3",
  C5: "c5.mp3",
} as const;

export function useIntervalAudio() {
  const samplerRef = useRef<Tone.Sampler | null>(null);
  const [audioState, setAudioState] = useState<AudioState>("idle");

  const getSampler = useCallback(() => {
    if (!samplerRef.current) {
      samplerRef.current = new Tone.Sampler({
        urls: GUITAR_SAMPLE_URLS,
        baseUrl: `${import.meta.env.BASE_URL}samples/guitar/`,
        release: 1.1,
        volume: -5,
      }).toDestination();
    }

    return samplerRef.current;
  }, []);

  const play = useCallback(
    async (question: IntervalQuestion) => {
      try {
        setAudioState("loading");
        await Tone.start();
        const sampler = getSampler();
        await Tone.loaded();

        setAudioState("playing");
        const now = Tone.now() + 0.08;
        const rootNote = midiToNoteName(question.rootMidi);
        const targetNote = midiToNoteName(question.targetMidi);

        sampler.releaseAll();

        if (question.mode === "harmonic") {
          sampler.triggerAttackRelease([rootNote, targetNote], 1.8, now, 0.95);
        } else {
          sampler.triggerAttackRelease(rootNote, 1.35, now, 0.95);
          sampler.triggerAttackRelease(targetNote, 1.35, now + 1.05, 0.95);
        }

        window.setTimeout(() => {
          setAudioState("idle");
        }, question.mode === "harmonic" ? 2100 : 2550);
      } catch {
        setAudioState("error");
      }
    },
    [getSampler],
  );

  useEffect(() => {
    return () => {
      samplerRef.current?.dispose();
      samplerRef.current = null;
    };
  }, []);

  return {
    audioState,
    play,
  };
}
