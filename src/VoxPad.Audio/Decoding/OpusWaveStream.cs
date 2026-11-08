using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;

namespace VoxPad.Audio.Decoding;

/// <summary>
/// Ogg Opus reader. Opus has no cheap random access without an index, so the file is decoded fully into memory
/// once (Opus clips are small: ~1 MB of PCM per 5 s) which makes seeking trivial. Output: 48 kHz stereo 16-bit PCM.
/// </summary>
public sealed class OpusWaveStream : WaveStream
{
    private readonly byte[] _pcm;
    private long _position;

    public OpusWaveStream(string path)
    {
        using var fs = File.OpenRead(path);
        var decoder = new OpusDecoder(48000, 2);
        var ogg = new OpusOggReadStream(decoder, fs);
        using var ms = new MemoryStream();
        while (ogg.HasNextPacket)
        {
            var packet = ogg.DecodeNextPacket();
            if (packet == null) continue;
            var bytes = new byte[packet.Length * 2];
            Buffer.BlockCopy(packet, 0, bytes, 0, bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        _pcm = ms.ToArray();
        WaveFormat = new WaveFormat(48000, 16, 2);
    }

    public override WaveFormat WaveFormat { get; }
    public override long Length => _pcm.Length;
    public override long Position { get => _position; set => _position = Math.Clamp(value - value % BlockAlign, 0, _pcm.Length); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = (int)Math.Min(count, _pcm.Length - _position);
        if (n <= 0) return 0;
        Buffer.BlockCopy(_pcm, (int)_position, buffer, offset, n);
        _position += n;
        return n;
    }
}
