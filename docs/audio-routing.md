# Audio routing design

## What Soundpad does, and why VoxPad does not do exactly that

Soundpad installs a kernel-mode audio filter (an APO/filter driver attached to the recording device) and injects audio into the *real* microphone stream. Every application that records from that microphone hears the sound without switching devices.

Doing the same requires:

* a kernel/APO driver built with the Windows Driver Kit,
* an EV code-signing certificate and Microsoft attestation signing (unsigned APOs do not load on Windows 10/11),
* an installer that stops/starts the audio service and reboots on first install.

None of that can be produced or installed from a user-mode .NET project, and shipping an unsigned driver would be dishonest. VoxPad therefore implements the fallback the specification allows: **a virtual microphone that mixes the physical microphone with the soundboard, with the requirement that applications select it documented everywhere** (Devices tab status line, troubleshooting dialog, manual, installer exit page).

The code is layered so a driver backend could be added later: `MicInjector` is the only class that knows where the mixed signal goes; the UI only sees `RoutingStatus` (mode, level, message, capture name).

## Signal flow

```
 physical mic(s)  ──► CapturePipeline (WASAPI shared, event-driven, 30 ms)
                      • converts any format/rate/channel count to 48 kHz stereo float
                      • ring buffer + drift compensation (±0.5 % rate trim)
                                  │
                                  ▼
                             VoiceGate  ──(Block voice: gain→0 in 5 ms while a sound plays)
                                  │
 sound file ──► DecodedSource ──► SlotSampleProvider (mic) ──► per-channel GainController (EBU R128) ──┐
                 (streamed, not loaded to RAM)   • optional pre-roll (voice activation)                  │
                                                                                                         ▼
                                                                                   MixingSampleProvider ──► WasapiOut → virtual cable "… Input"
                                                                                                                             │
                                                                                                      apps record from "… Output" ◄┘
 sound file ──► DecodedSource ──► SlotSampleProvider (speakers) ──► WasapiOut → chosen playback device (Windows mixer session)
```

* **Everything is 48 kHz stereo float** inside the engine. Decoders: Media Foundation (MP3, AAC/M4A/MP4, WMA, FLAC), `WaveFileReader` (WAV), NVorbis (OGG), Concentus (Opus; decoded fully, it is the only format without cheap seeking).
* **Two independent readers** per sound (mic + speakers) keep memory flat and make Seek trivial; both are opened before the previous sound is stopped so the swap is gapless.
* **Position** is counted in output frames after the last seek, so it is sample-accurate regardless of resampling.
* **One sound at a time**: `SlotSampleProvider.Load` replaces whatever is playing on that output. `SoundboardEngine.Play` stops both outputs first.

## Setup modes

| Mode | Render endpoint used | Apps select | Detection |
|------|----------------------|-------------|-----------|
| Auto-select | first mode below that reports Good | – | tries in table order |
| VB-CABLE | `CABLE Input` | `CABLE Output` | friendly-name match |
| VB-CABLE A/B | `CABLE-A Input` / `CABLE-B Input` | `CABLE-A Output` / `CABLE-B Output` | friendly-name match |
| Voicemeeter | `VoiceMeeter Input` / `Aux Input` / `VAIO3 Input` | the matching `… Output` | friendly-name match |
| Other virtual cable | user-picked playback endpoint | its recording twin | manual |
| Stereo Mix / What U Hear | default speakers | `Stereo Mix` | capture-name match; warns that *all* PC audio is heard |
| Speakers only | none | – | always available |

`DeviceManager.Resolve` returns a `RoutingStatus` with level **Good**, **Warning** (works, with a caveat such as "no microphone ticked" or Stereo Mix) or **Error** (red) and a human-readable reason:

* virtual cable not installed / endpoint disabled,
* selected microphone unplugged or disabled,
* Windows microphone privacy denies desktop apps (consent store `CapabilityAccessManager\ConsentStore\microphone[\NonPackaged]`),
* render endpoint cannot be opened (exclusive-mode app, format).

Hot-plug: `IMMNotificationClient` events (debounced 300 ms) re-resolve and restart both outputs; the status bar reports it.

## Default devices and VB-CABLE

VB-CABLE's installer sets its endpoints as the Windows default playback **and** recording devices. If VoxPad simply used "the default", the speaker copy would be rendered into the cable too (others would hear everything twice, unnormalised) and the "microphone" would be the cable's own output. `DeviceManager.PhysicalDefault` therefore skips any default endpoint whose name looks like a virtual cable and uses the first physical one, logging the substitution. The acceptance helper `voxpad-cablecheck setdefault …` puts the user's real devices back as Windows defaults.

## Mixing modes

* **Mix**: voice + sound summed; the mixer output is soft-clipped at ±0.9…1.0 so a normalisation boost cannot overdrive the cable.
* **Block voice**: `VoiceGate` ramps the voice to silence while `SlotSampleProvider.IsActive` and back afterwards (5 ms linear ramps, no clicks).

## Normalisation (EBU R128)

`EbuR128Meter` implements BS.1770-4: K-weighting (shelf + RLB high-pass, coefficients derived for the engine rate), 400 ms blocks with 75 % overlap, absolute gate −70 LUFS, relative gate −10 LU. Each channel is measured as a mono programme so left and right get **their own gain**; a silent channel borrows the other's gain.

* **Soundpad dB scale**: `dB = LUFS + 112` (89 dB ≈ −23 LUFS). The voice measurement records ~8 s of the microphone and reports the integrated value; the spec's target of **> 74 dB** means −38 LUFS or louder.
* **Dynamic**: decode the first 6 s (not played), set the gain, then a live meter on the actual stream refines the gain once per second (50 % blend, 40 ms smoothing in `GainController`).
* **Full file**: measure the whole file once, cache the result (`cache/loudness.json`, keyed by path+size+mtime).
* The gain is applied to the **microphone path only**; what you hear on the speakers is the file as-is at your speaker volume.

## Latency and drift

The physical microphone and the virtual cable are different WASAPI devices with different clocks. Without compensation the capture ring either fills up (growing latency, an audible skip every few minutes when it overflows) or runs dry (periodic dropouts). `CapturePipeline` measures the fill level against a target (≈ the configured latency) and trims the read rate by up to ±0.5 % with linear interpolation, which is inaudible on speech and keeps the voice delay constant at roughly the configured 30 ms plus the cable's own buffer.

## Speaker volume

`SpeakerOutput` finds the process's audio session on the playback device (`IAudioSessionManager2` → `ISimpleAudioVolume`) and binds the toolbar slider to it, so the slider and the Windows Volume Mixer always agree. The microphone path is a separate session on the cable device and is never affected by that slider.

## Recorder

What-you-hear uses `WasapiLoopbackCapture` on the playback device; a silent `WasapiOut` keeps the device busy so loopback delivers data during silence. Audio is written as raw float to `%TEMP%\VoxPad` while recording (RAM stays flat), then trimmed, normalised and encoded (Media Foundation AAC/MP3 or 16-bit WAV).
