//using System;
using System.Text;

namespace XRoadFolkRaw
{
    internal static class ConsoleInput
    {
        internal static Func<ConsoleKeyInfo> ReadKey { get; set; } = () => Console.ReadKey(intercept: true);

        public static string? ReadLineOrCtrlQ(out bool quit)
        {
            StringBuilder buf = new();
            quit = false;
            while (true)
            {
                ConsoleKeyInfo key = ReadKey();
                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Q)
                {
                    Console.WriteLine();
                    quit = true;
                    return null;
                }
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return buf.ToString();
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buf.Length > 0)
                    {
                        buf.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }
                if (char.IsControl(key.KeyChar))
                {
                    continue;
                }
                _ = buf.Append(key.KeyChar);
                Console.Write(key.KeyChar);
            }
        }

        public static string? ReadPasswordOrCtrlQ(out bool quit, char maskChar = '*')
        {
            StringBuilder buf = new();
            quit = false;
            while (true)
            {
                ConsoleKeyInfo key = ReadKey();
                if ((key.Modifiers & ConsoleModifiers.Control) != 0 && key.Key == ConsoleKey.Q)
                {
                    Console.WriteLine();
                    quit = true;
                    return null;
                }
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return buf.ToString();
                }
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (buf.Length > 0)
                    {
                        buf.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }
                if (char.IsControl(key.KeyChar))
                {
                    continue;
                }
                _ = buf.Append(key.KeyChar);
                Console.Write(maskChar);
            }
        }
    }
}
