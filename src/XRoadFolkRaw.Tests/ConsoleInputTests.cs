using System;
using System.Collections.Generic;
using Xunit;
using XRoadFolkRaw;

namespace XRoadFolkRaw.Tests
{
    public class ConsoleInputTests
    {
        [Fact]
        public void ReadLineOrCtrlQ_AllowsSingleLetterQ()
        {
            var keys = new Queue<ConsoleKeyInfo>(new[]
            {
                new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false),
                new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            });

            var original = ConsoleInput.ReadKey;
            ConsoleInput.ReadKey = () => keys.Dequeue();
            try
            {
                string? result = ConsoleInput.ReadLineOrCtrlQ(out bool quit);
                Assert.False(quit);
                Assert.Equal("Q", result);
            }
            finally
            {
                ConsoleInput.ReadKey = original;
            }
        }

        [Fact]
        public void ReadLineOrCtrlQ_QuitsOnCtrlQ()
        {
            var keys = new Queue<ConsoleKeyInfo>(new[]
            {
                new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, true)
            });

            var original = ConsoleInput.ReadKey;
            ConsoleInput.ReadKey = () => keys.Dequeue();
            try
            {
                string? result = ConsoleInput.ReadLineOrCtrlQ(out bool quit);
                Assert.True(quit);
                Assert.Null(result);
            }
            finally
            {
                ConsoleInput.ReadKey = original;
            }
        }
    }
}
