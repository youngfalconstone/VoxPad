using VoxPad.Core;
using VoxPad.Core.Models;
using VoxPad.Core.Remote;
using VoxPad.Core.Serialization;
using VoxPad.Core.Settings;
using Xunit;

namespace VoxPad.Tests;

public class NaturalSortTests
{
    [Fact]
    public void NumbersSortNumerically()
    {
        var items = new[] { "sound10", "sound2", "Sound1", "sound1a" };
        var sorted = items.OrderBy(x => x, NaturalStringComparer.Instance).ToArray();
        Assert.Equal(new[] { "Sound1", "sound1a", "sound2", "sound10" }, sorted);
    }

    [Fact]
    public void CaseInsensitive() => Assert.Equal(0, NaturalStringComparer.Instance.Compare("Air Horn", "air horn"));
}

public class SoundListTests
{
    private static SoundList Sample()
    {
        var list = new SoundList();
        var cat = new Category { Name = "Memes", Color = 0xFFFF0000 };
        cat.Children.Add(new Category { Name = "Nested", RandomHotkey = Hotkey.Key(0x72, HotkeyModifiers.Ctrl) });
        list.Categories.Add(cat);
        for (int i = 1; i <= 5; i++)
        {
            var s = new SoundEntry { FilePath = $@"C:\sounds\s{i}.mp3", Title = $"Title {i}", Artist = i % 2 == 0 ? "Artist" : null, DurationMs = i * 1000, Color = (uint)i, PlayCount = i, Hotkey = i == 3 ? Hotkey.Key(0x70 + i) : null };
            list.Add(s, i <= 3 ? cat : null);
        }
        list.HotbarTiles.Add(new HotbarTile { Page = 1, Slot = 2, SoundId = list.AllSoundsOrder[0], Color = 0xFF00FF00 });
        list.TotalPlays = 15;
        return list;
    }

    [Fact]
    public void RoundTripPreservesEverything()
    {
        var list = Sample();
        var json = SoundListFile.Serialize(list);
        var back = SoundListFile.Deserialize(json);
        Assert.Equal(list.AllSoundsOrder, back.AllSoundsOrder);
        Assert.Equal(list.Sounds.Count, back.Sounds.Count);
        Assert.Equal("Artist – Title 2", back.ByIndex(2)!.Tag);
        Assert.Equal("Title 1", back.ByIndex(1)!.Tag);
        Assert.Equal(Hotkey.Key(0x73), back.ByIndex(3)!.Hotkey);
        Assert.Equal(3, back.Categories[0].SoundIds.Count);
        Assert.Equal("Nested", back.Categories[0].Children[0].Name);
        Assert.Equal(Hotkey.Key(0x72, HotkeyModifiers.Ctrl), back.Categories[0].Children[0].RandomHotkey);
        Assert.Single(back.HotbarTiles);
        Assert.Equal(0xFF00FF00u, back.HotbarTiles[0].Color);
        Assert.Equal(15, back.TotalPlays);
        Assert.Equal(0xFFFF0000u, back.Categories[0].Color);
    }

    [Fact]
    public void IndexIsOneBasedPositionInAllSounds()
    {
        var list = Sample();
        Assert.Equal(1, list.IndexOf(list.AllSoundsOrder[0]));
        Assert.Equal("Title 5", list.ByIndex(5)!.Tag);
        Assert.Null(list.ByIndex(6));
        Assert.Null(list.ByIndex(0));
    }

    [Fact]
    public void RemoveCleansCategoriesAndHotbar()
    {
        var list = Sample();
        var id = list.AllSoundsOrder[0];
        list.Remove(id);
        Assert.DoesNotContain(id, list.Categories[0].SoundIds);
        Assert.Empty(list.HotbarTiles);
        Assert.Equal(4, list.AllSoundsOrder.Count);
    }

    [Fact]
    public void TagFallsBackToFileName()
    {
        var s = new SoundEntry { FilePath = @"C:\x\my sound.wav" };
        Assert.Equal("my sound", s.Tag);
    }

    [Fact]
    public void RelativeExportResolvesOnLoad()
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxpad-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var list = new SoundList();
            var file = Path.Combine(dir, "a.wav");
            File.WriteAllBytes(file, new byte[10]);
            list.Add(new SoundEntry { FilePath = file });
            var listPath = Path.Combine(dir, "list.vpl");
            SoundListFile.SaveRelative(list, listPath);
            Assert.Contains("\"Path\": \"a.wav\"", File.ReadAllText(listPath));
            // Simulate the second PC: move the folder.
            var dir2 = dir + "-moved";
            Directory.Move(dir, dir2);
            var loaded = SoundListFile.Load(Path.Combine(dir2, "list.vpl"));
            Assert.Equal(Path.Combine(dir2, "a.wav"), loaded.Sounds[0].FilePath);
            Directory.Delete(dir2, true);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void ImportsSoundpadXml()
    {
        var path = Path.GetTempFileName() + ".spl";
        File.WriteAllText(path, """
            <?xml version="1.0"?>
            <Soundlist>
              <Sounds>
                <Sound url="C:\s\one.mp3" index="1" artist="A" title="One" duration="1500" playCount="7" hotkey="Ctrl+F5" color="#FF0000"/>
                <Sound url="C:\s\two.mp3" index="2" title="Two" duration="2500" hotkey="Num 3"/>
              </Sounds>
              <Categories>
                <Category name="Cat"><Sound index="2"/><Category name="Sub"><Sound index="1"/></Category></Category>
              </Categories>
            </Soundlist>
            """);
        try
        {
            var list = SoundListFile.Load(path);
            Assert.Equal(2, list.Sounds.Count);
            Assert.Equal("A – One", list.ByIndex(1)!.Tag);
            Assert.Equal(Hotkey.Key(0x74, HotkeyModifiers.Ctrl), list.ByIndex(1)!.Hotkey);
            Assert.Equal(Hotkey.Key(0x63), list.ByIndex(2)!.Hotkey);
            Assert.Equal(0xFFFF0000u, list.ByIndex(1)!.Color);
            Assert.Single(list.Categories);
            Assert.Equal("Sub", list.Categories[0].Children[0].Name);
            Assert.Equal(list.ByIndex(1)!.Id, list.Categories[0].Children[0].SoundIds[0]);
        }
        finally { File.Delete(path); }
    }
}

public class HotkeyTests
{
    [Fact]
    public void SerializeRoundTrip()
    {
        var hk = Hotkey.Mouse(HotkeyInput.MouseX1, HotkeyModifiers.Ctrl | HotkeyModifiers.Shift);
        Assert.Equal(hk, Hotkey.Deserialize(hk.Serialize()));
    }

    [Fact]
    public void BareLeftRightMouseNotAllowed()
    {
        Assert.False(Hotkey.Mouse(HotkeyInput.MouseLeft).IsAllowed);
        Assert.False(Hotkey.Mouse(HotkeyInput.MouseRight).IsAllowed);
        Assert.True(Hotkey.Mouse(HotkeyInput.MouseLeft, HotkeyModifiers.Ctrl).IsAllowed);
        Assert.True(Hotkey.Mouse(HotkeyInput.MouseMiddle).IsAllowed);
    }

    [Fact]
    public void NormalizeFoldsSides()
    {
        var left = Hotkey.Key(0x41, HotkeyModifiers.Ctrl | HotkeyModifiers.LeftCtrl);
        var right = Hotkey.Key(0x41, HotkeyModifiers.Ctrl | HotkeyModifiers.RightCtrl);
        Assert.Equal(left.Normalize(false), right.Normalize(false));
        Assert.NotEqual(left.Normalize(true), right.Normalize(true));
    }

    [Fact]
    public void ToStringIsReadable()
    {
        Assert.Equal("Ctrl+Alt+F5", Hotkey.Key(0x74, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt).ToString());
        Assert.Equal("Shift+Mouse 4", Hotkey.Mouse(HotkeyInput.MouseX1, HotkeyModifiers.Shift).ToString());
        Assert.Equal("Num 7", Hotkey.Key(0x67).ToString());
    }

    [Fact]
    public void DigitValues()
    {
        Assert.Equal(3, VirtualKeyNames.DigitValue(0x33));
        Assert.Equal(3, VirtualKeyNames.DigitValue(0x63));
        Assert.Equal(-1, VirtualKeyNames.DigitValue(0x41));
        Assert.True(VirtualKeyNames.IsTopRowDigit(0x31));
        Assert.False(VirtualKeyNames.IsNumpadDigit(0x31));
    }
}

public class PreferencesTests
{
    [Fact]
    public void DefaultsMatchSpec()
    {
        var p = new Preferences();
        Assert.Equal(NormalizationMode.Dynamic, p.Normalization);
        Assert.Equal(74, p.NormalizationTargetDb);
        Assert.True(p.AutoStop);
        Assert.False(p.PassHotkeys);
        Assert.Equal(HotkeyModifiers.Alt, p.NumpadModifier);
        Assert.Equal(RecorderFormat.M4A, p.RecorderFormat);
        Assert.Equal(89, p.RecorderNormalizeTargetDb);
        Assert.Equal(300, p.RecorderMaxSeconds);
        Assert.Equal(UpdateCheckInterval.Never, p.UpdateCheck);
        Assert.Equal(MicSetupMode.Auto, p.SetupMode);
        Assert.True(p.AutoKeysEnabled);
    }

    [Fact]
    public void SaveLoadExportRoundTrip()
    {
        var dir = Path.Combine(Path.GetTempPath(), "voxpad-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var p = new Preferences { Language = "de", JumpSeconds = 9, SpeakerDeviceId = "dev1" };
            p.Special.Stop = Hotkey.Key(0x13);
            p.AutoKeys.Add(new AutoKeyEntry { Key = Hotkey.Key(0x56), Condition = AutoKeyCondition.ListedAppForeground, Apps = { "discord.exe" } });
            var file = Path.Combine(dir, "p.json");
            p.Save(file);
            var back = Preferences.Load(file);
            Assert.Equal("de", back.Language);
            Assert.Equal(9, back.JumpSeconds);
            Assert.Equal(Hotkey.Key(0x13), back.Special.Stop);
            Assert.Equal("discord.exe", back.AutoKeys[0].Apps[0]);
            var exp = Path.Combine(dir, "e.vpprefs");
            p.Export(exp);
            var imported = Preferences.Import(exp);
            Assert.Equal("", imported.SpeakerDeviceId); // machine-specific ids are stripped
            Assert.Equal(9, imported.JumpSeconds);
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class ProtocolTests
{
    [Fact]
    public void ParsesShorthandAndJson()
    {
        var r = Protocol.Parse("play 12 mic");
        Assert.Equal(Protocol.Command.Play, r.Cmd);
        Assert.Equal(12, r.Index);
        Assert.Equal(Protocol.Target.Mic, r.Target);
        var j = Protocol.Parse("{\"cmd\":\"playMode\",\"mode\":\"speakers\"}");
        Assert.Equal(Protocol.Command.PlayMode, j.Cmd);
        Assert.Equal("speakers", j.Mode);
        var n = Protocol.Parse("play air horn");
        Assert.Equal("air horn", n.Name);
    }

    [Fact]
    public void ResponseSerializesCamelCase()
    {
        var s = Protocol.Serialize(new Protocol.Response { Ok = true, Sounds = new() { new Protocol.SoundInfo { Index = 1, Tag = "x", Color = "#FF0000" } } });
        Assert.Contains("\"ok\":true", s);
        Assert.Contains("\"tag\":\"x\"", s);
        Assert.Contains("\"color\":\"#FF0000\"", s);
    }
}
