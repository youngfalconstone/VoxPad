using VoxPad.Audio.Decoding;
using VoxPad.Audio.Devices;
using VoxPad.Audio.Engine;
using VoxPad.Core.Loudness;

// Acceptance-test helper: records a recording endpoint (default: the VB-CABLE output, i.e. what Discord hears)
// for N seconds and prints peak, RMS, integrated loudness and the dominant frequency.
//   voxpad-cablecheck [seconds] ["device name substring"]
//   voxpad-cablecheck 3 "CABLE Output"       → while VoxPad plays a sound on the microphone
//   voxpad-cablecheck list                   → list recording endpoints

using var dm = new DeviceManager();
if (args.Length > 2 && args[0] == "setdefault")
{
    // voxpad-cablecheck setdefault render|capture "<name substring>" [comms]
    var pool = args[1] == "render" ? dm.RenderDevices : dm.CaptureDevices;
    var target = pool.FirstOrDefault(d => d.IsActive && d.Name.Contains(args[2], StringComparison.OrdinalIgnoreCase));
    if (target == null) { Console.Error.WriteLine("No active device matching " + args[2]); return 1; }
    VoxPad.CableCheck.PolicyConfig.SetDefault(target.Id, args.Length > 3 && args[3] == "comms");
    Console.WriteLine($"Default {args[1]} device → {target.Name}");
    return 0;
}
bool timeline = args.Contains("timeline");
args = args.Where(a => a != "timeline").ToArray();
if (args.Length > 0 && args[0] == "list")
{
    Console.WriteLine("Recording:");
    foreach (var d in dm.CaptureDevices) Console.WriteLine($"  [{d.State}]{(d.IsDefault ? " default" : "")}{(d.IsDefaultCommunications ? " comms" : "")} {d.Name}");
    Console.WriteLine("Playback:");
    foreach (var d in dm.RenderDevices) Console.WriteLine($"  [{d.State}]{(d.IsDefault ? " default" : "")}{(d.IsDefaultCommunications ? " comms" : "")} {d.Name}");
    return 0;
}
double seconds = args.Length > 0 && double.TryParse(args[0], out var s) ? s : 3;
string name = args.Length > 1 ? args[1] : "CABLE Output";
var info = dm.CaptureDevices.FirstOrDefault(d => d.IsActive && d.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
if (info == null) { Console.Error.WriteLine($"No active recording device matching '{name}'. Use 'list'."); return 1; }
using var dev = dm.GetDevice(info.Id)!;
using var pipe = new CapturePipeline(dev, loopback: false, 30);
var meter = new EbuR128Meter(EngineFormat.SampleRate, 2);
pipe.Start();
Console.WriteLine($"Recording '{info.Name}' for {seconds:F1} s…");
var buf = new float[4800];
long frames = 0, total = (long)(seconds * EngineFormat.SampleRate);
float peak = 0; double sum = 0; int crossings = 0; float prev = 0;
var sw = System.Diagnostics.Stopwatch.StartNew();
while (frames < total)
{
    int n = pipe.Read(buf, 0, buf.Length);
    meter.Process(new ReadOnlySpan<float>(buf, 0, n));
    for (int i = 0; i < n; i += 2)
    {
        float v = buf[i];
        peak = Math.Max(peak, Math.Abs(v)); sum += v * v;
        if ((prev < 0 && v >= 0) || (prev >= 0 && v < 0)) crossings++;
        prev = v;
    }
    frames += n / 2;
    if (timeline && frames % 4800 == 0) { float p = 0; for (int i = 0; i < n; i += 2) p = Math.Max(p, Math.Abs(buf[i])); Console.Write($"{p:F2} "); }
    var wait = TimeSpan.FromSeconds(frames / (double)EngineFormat.SampleRate) - sw.Elapsed;
    if (wait > TimeSpan.Zero) Thread.Sleep(wait);
}
double rms = Math.Sqrt(sum / Math.Max(1, frames));
double lufs = meter.IntegratedLufs();
Console.WriteLine($"peak={peak:F3}  rms={rms:F3} ({20 * Math.Log10(Math.Max(rms, 1e-9)):F1} dBFS)  loudness={(double.IsNaN(lufs) ? "n/a" : $"{lufs:F1} LUFS = {EbuR128Meter.LufsToDb(lufs):F1} dB")}  dominant≈{crossings / 2.0 / seconds:F0} Hz");
Console.WriteLine(peak < 0.001 ? "RESULT: silence" : "RESULT: audio present");
return 0;
