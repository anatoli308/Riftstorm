using System;

namespace Tolik.Riftstorm.Runtime.ApplicationLifecycle
{
    /// <summary>
    /// Parses the relevant command line arguments for a dedicated server build:
    /// <list type="bullet">
    ///   <item><c>--port &lt;ushort&gt;</c> — NGO listen port (default 7777).</item>
    ///   <item><c>--target-framerate &lt;int&gt;</c> — server tick / framerate (default 30).</item>
    ///   <item><c>--listen-address &lt;string&gt;</c> — bind address (default 0.0.0.0).</item>
    /// </list>
    /// </summary>
    public class CommandLineArgumentsParser
    {
        public int Port { get; } = 7777;
        public int TargetFramerate { get; } = 30;
        public string ListenAddress { get; } = "0.0.0.0";

        public CommandLineArgumentsParser()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int port))
                        {
                            Port = port;
                        }
                        break;
                    case "--target-framerate":
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int fps))
                        {
                            TargetFramerate = fps;
                        }
                        break;
                    case "--listen-address":
                        if (i + 1 < args.Length)
                        {
                            ListenAddress = args[i + 1];
                        }
                        break;
                }
            }
        }
    }
}
