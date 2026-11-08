using System.IO.Pipes;
using System.Text;
using VoxPad.Core.Remote;

// Reference client for the VoxPad Remote Control API.
//   voxpad-remote list
//   voxpad-remote play 3            (index in "All sounds")
//   voxpad-remote play "air horn"   (name; substring match)
//   voxpad-remote play 3 mic        (target: auto|mic|speakers|both)
//   voxpad-remote stop | pause | status | version | random | categories
//   voxpad-remote playmode mic      (both|mic|speakers)
//   voxpad-remote volume 0.5
//   voxpad-remote raw '{"cmd":"play","index":1,"target":"mic"}'
//   voxpad-remote                   (interactive shell)
// Stream Deck / Touch Portal plugins: open \\.\pipe\VoxPadRemote, write one JSON line, read one JSON line.

if (args.Length > 0 && args[0] is "-h" or "--help" or "/?")
{
    Console.WriteLine("usage: voxpad-remote [command [args]]   (no command = interactive)");
    Console.WriteLine("commands: version status list categories play <index|name> [auto|mic|speakers|both] stop pause random playmode [both|mic|speakers] volume [0..1] raw <json>");
    return 0;
}

try
{
    using var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut, PipeOptions.None);
    pipe.Connect(3000);
    using var reader = new StreamReader(pipe, new UTF8Encoding(false));
    using var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };

    string Send(string line)
    {
        writer.WriteLine(line);
        return reader.ReadLine() ?? throw new IOException("VoxPad closed the pipe");
    }

    if (args.Length == 0)
    {
        Console.WriteLine("Connected to VoxPad. Type commands (help: list, play 3, stop, status, quit).");
        while (true)
        {
            Console.Write("> ");
            var line = Console.ReadLine();
            if (line == null || line.Trim() is "quit" or "exit") break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            Console.WriteLine(Send(line));
        }
        return 0;
    }

    string request = args[0].Equals("raw", StringComparison.OrdinalIgnoreCase) && args.Length > 1 ? args[1] : string.Join(' ', args);
    var response = Send(request);
    Console.WriteLine(response);
    return response.Contains("\"ok\":false") ? 2 : 0;
}
catch (TimeoutException)
{
    Console.Error.WriteLine("VoxPad is not running (pipe \\\\.\\pipe\\" + Protocol.PipeName + " not found).");
    return 1;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
