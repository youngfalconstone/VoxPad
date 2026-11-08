# 01 – Sound audible in Discord with all Discord processing disabled

Verifies the core promise: a sound played by VoxPad reaches the other side of a Discord call, with and without your voice mixed in, and that the voice-activation pre-roll opens Discord's voice gate.

## Preconditions

- VB-CABLE installed and the PC rebooted once after installing it.
- VoxPad installed, started normally (not as administrator).
- Preferences > Devices: Setup mode = Auto-select, Status reads **Good. Apps must select 'CABLE Output' as their microphone.** Your real microphone is ticked under **Use**. Speakers = your headphones.
- A sound list with at least one clearly recognisable clip of 3–5 s (call it *Clip A*), played once on the speakers to confirm it decodes.
- A second person (or a second Discord account on another device) in a voice call with you. Call them *Listener*.
- Discord > User Settings > Voice & Video on the VoxPad PC:
  - Input Device = **CABLE Output (VB-Audio Virtual Cable)**
  - Output Device = your headphones
  - Input Mode = **Voice Activity**, **Automatically determine input sensitivity = OFF**, slider at about −60 dB (far left) for this test
  - Noise Suppression = **None** (Krisp off), Echo Cancellation = **OFF**, Noise Reduction = **OFF**, Automatic Gain Control = **OFF**, Advanced Voice Activity = **OFF**
- Play mode in VoxPad's status bar = **Speakers + Microphone**. Preferences > Audio: Block voice **off**, Voice activation sound **off**.

## Steps

### Part A – Mix mode (voice + sound)

1. Say "test one two" in the call.
   **Expected:** Listener hears your voice normally (proves the mic is mixed into the cable). The green mic bar in Discord moves while you speak.
2. Select *Clip A* in the list and press **Enter**.
   **Expected:** You hear Clip A on your headphones; the seek bar fills **red**; Listener hears Clip A at a level similar to your voice.
3. While Clip A plays, keep talking.
   **Expected:** Listener hears both your voice and the clip at the same time (mixed, neither dropped).
4. Press **Enter** again on a different clip while Clip A is still playing.
   **Expected:** Clip A stops immediately and the new clip plays (one sound at a time; no overlap).
5. Set Play mode to **Microphone only** (click the status-bar indicator twice or Play > Play mode) and play Clip A.
   **Expected:** You hear nothing on your headphones; the seek bar is still red; Listener hears the clip.
6. Set Play mode to **Speakers only** and play Clip A.
   **Expected:** You hear it; the seek bar is **blue**; Listener hears nothing (and Discord's input indicator does not light from the clip).
7. Return to **Speakers + Microphone**.

### Part B – Block voice (sound replaces voice)

8. Preferences > Audio > tick **Replace my voice entirely while a sound plays**, press OK.
9. Start talking continuously, then press Clip A's hotkey (or Enter) and keep talking through the whole clip.
   **Expected:** Listener hears your voice up to the clip start, then only the clip (your voice is muted, no clicks at the transition), then your voice again as soon as the clip ends.
10. Untick Block voice afterwards.

### Part C – Voice-activation pre-roll with Discord Voice Activity

11. In Discord set the Input Sensitivity slider to about **−30 dB** (high threshold) so that quiet audio does not open the gate. Confirm by whispering: the gate should stay closed.
12. Pick a quiet clip (fade-in, or lower a clip by −20 dB in the Editor and save it as *Clip Q*). Play Clip Q to the mic.
    **Expected:** Listener reports the start is missing or the clip is not heard at all (gate closed).
13. Preferences > Audio > tick **Play a short loud pre-roll on the microphone …**, leave the custom file empty, press OK. Play Clip Q again.
    **Expected:** Listener hears a short 1 kHz blip (~250 ms) followed by the whole clip from its first sample; the gate opened on the blip. You do **not** hear the blip on your headphones.
14. Tick **Also play the pre-roll on my speakers/headphones**, play Clip Q.
    **Expected:** You now hear the blip too.
15. Set a custom file (any short loud WAV) and play Clip Q.
    **Expected:** Listener hears that file instead of the tone, then the clip.
16. Untick the voice-activation options and restore Discord's sensitivity.

### Part D – Negative check (filters on)

17. In Discord turn Noise Suppression back to **Krisp** and play a music-like clip.
    **Expected (informational):** Listener hears the clip heavily attenuated or garbled. This confirms the checklist precondition is needed; turn Krisp off again.

## Expected results summary

- Steps 1–4, 9, 13 pass → microphone injection works in Mix and Block voice modes.
- Step 5/6 confirm play modes route correctly and the seek bar colour follows.
- Step 13 shows the pre-roll opens a VAD gate that a quiet clip alone cannot.

## Evidence to capture

- Screenshot of Preferences > Devices with the green Status line.
- Screenshot of Discord Voice & Video settings (input device and all processing off).
- Listener's written confirmation per step (or a recording of the call from the Listener's side with Discord's "Record" bot / OBS).
- Help > Troubleshooting > Create report → attach the zip.
