# 07 – Seek bar colour matches the output target

Verifies that the toolbar seek slider is **red** exactly when the sound is audible to others (it is on the microphone path) and **blue** when it only reaches your own speakers, and that the status-bar routing dot reflects the cable state. The colour is driven by `AudibleToOthers` (the microphone slot holds a source), not by the play-mode setting alone – so with no cable it must be blue even in "Speakers + Microphone" mode.

## Preconditions

- VoxPad with Preferences > Interface > Style = **Light** first (repeat the key steps in Dark and Native; the red/blue hues differ slightly per theme but must stay unmistakably red vs blue).
- Toolbar items Seek slider and Track timer visible (Preferences > Interface).
- Part A needs VB-CABLE installed and Status = Good; Part B needs it absent/disabled.
- A 10 s clip (*Clip L*) so there is time to look at the bar. A Listener in a call (or OBS recording CABLE Output) to confirm audibility.

## Part A – cable present (Status: Good, green dot)

1. Hover the status-bar dot.
   **Expected:** Green dot; tooltip "Good. Apps must select 'CABLE Output' as their microphone."
2. Play mode **Speakers + Microphone**. Play Clip L.
   **Expected:** Seek fill and thumb are **red**; the bar advances with the timer; Listener hears it. Dragging the thumb seeks (timer jumps, audio jumps) and the colour stays red.
3. Play mode **Microphone only**. Play Clip L.
   **Expected:** **Red**; you hear nothing on your speakers; Listener hears it.
4. Play mode **Speakers only**. Play Clip L.
   **Expected:** **Blue**; you hear it; Listener hears nothing.
5. Back in Speakers + Microphone, use the toolbar buttons: **▶🔊 Play on speakers** → **blue**; **▶🎤 Play on microphone** → **red** (the forced target wins over the play mode).
6. Pause (Space) while red.
   **Expected:** Stays red while paused; after resume still red. Stop → the bar empties (fraction 0), colour irrelevant.
7. Play Clip L to the microphone, then switch play mode to Speakers only *while it plays*.
   **Expected:** Colour does not change mid-sound (red, the running sound is still on the mic); the next sound started is blue.
8. Preferences > Audio > *Allow resume in another play mode* on. Play to the mic (red), pause, switch play mode to Speakers only, press Space.
   **Expected:** The sound restarts at the paused position on the speakers only and the bar turns **blue** (Listener stops hearing it).
9. TTS (Ctrl+T): type a sentence, press **Enter** → blue (speakers preview); press **Ctrl+Enter** → red.
10. Editor: right-click a sound > Edit file…, press Space to play the selection.
    **Expected:** The main window's seek bar is **blue** (editor previews are always speakers-only), and the Listener hears nothing.
11. Trial variant (unregistered, after 10 mic plays): press Enter in Speakers + Microphone mode.
    **Expected:** Refused – nothing plays and the bar stays empty (no red fill ever appears for a refused play).

## Part B – cable absent

12. Disable the cable: Windows Sound settings > Playback > right-click **CABLE Input** > Disable (or uninstall VB-CABLE). In VoxPad press **Refresh** in Preferences > Devices.
    **Expected:** Status red: "No virtual microphone found. Install VB-CABLE…" (or "'CABLE Input' is disabled in Windows Sound settings…" if only disabled). Status-bar dot **red**; hovering shows that text; the status bar shows the message in red.
13. Play mode **Speakers + Microphone**. Play Clip L.
    **Expected:** The sound plays on your speakers and the seek bar is **blue** – nothing reaches others, so the bar must not claim it does. Status bar "Playing 'Clip L' → Speakers".
14. Play mode **Microphone only**. Press Enter.
    **Expected:** Nothing plays; red status "Microphone output is not available: No virtual microphone found…". The bar stays empty.
15. Toolbar **▶🎤 Play on microphone**.
    **Expected:** Same refusal as step 14.
16. Re-enable CABLE Input, press Refresh.
    **Expected:** Dot green again; step 2 gives red again without restarting VoxPad.
17. Hot-plug variant: with a sound playing red, disable CABLE Input in Windows Sound settings.
    **Expected:** Within ~1 s VoxPad logs/announces "Microphone output stopped; re-applying devices." or "Audio devices changed; routing re-applied.", the sound stops, the dot turns red; the next play is blue.

## Part C – themes

18. Repeat steps 2 and 4 with Style = **Dark** and **Native**.
    **Expected:** Red (`#D9342B` light / `#E8534A` dark) vs blue (`#2B6DD9` light / `#4C8DFF` dark) – always distinguishable; in Native (and under Windows High Contrast) the same red/blue brushes are used.

## Expected results summary

- Red ⇔ the current sound is on the microphone path (cable present and target includes mic); blue otherwise, including Speakers + Microphone mode with no cable, TTS preview, editor preview.
- The routing dot is green only when Status is Good.
- Colour follows the actually running sound, not the play-mode indicator.

## Evidence to capture

- Screenshots of the toolbar during steps 2, 4, 13 (include the status bar with the dot and play-mode indicator).
- Screenshot of Preferences > Devices in step 12.
- Listener confirmation for steps 2–4 and 13.
- Log excerpt for step 17.
