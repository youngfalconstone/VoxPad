namespace VoxPad.Core.Models;

/// <summary>Where a plain "Play" sends audio. Mic play modes are the only ones that fire Auto Keys and count stats.</summary>
public enum PlayMode
{
    /// <summary>Speakers and microphone.</summary>
    Both = 0,
    /// <summary>Microphone only (others hear it, you do not).</summary>
    MicrophoneOnly = 1,
    /// <summary>Speakers only (only you hear it).</summary>
    SpeakersOnly = 2,
}

public static class PlayModeExtensions
{
    public static bool IncludesMic(this PlayMode m) => m is PlayMode.Both or PlayMode.MicrophoneOnly;
    public static bool IncludesSpeakers(this PlayMode m) => m is PlayMode.Both or PlayMode.SpeakersOnly;
    public static PlayMode Next(this PlayMode m) => (PlayMode)(((int)m + 1) % 3);
}
