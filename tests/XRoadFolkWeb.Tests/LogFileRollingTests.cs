using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using XRoadFolkWeb.Infrastructure;

namespace XRoadFolkWeb.Tests
{
    public class LogFileRollingTests
    {
        [Fact]
        public void Rolls_When_Exceeds_Size()
        {
            string dir = Path.Combine(Path.GetTempPath(), "xrf_logroll_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "test.log");
            File.WriteAllText(path, new string('A', 200));
            // maxBytes small to trigger roll
            LogFileRolling.RollIfNeeded(path, maxBytes: 100, maxRolls: 2, log: NullLogger.Instance);
            File.Exists(path + ".1").Should().BeTrue();
        }

        [Fact]
        public void Old_Rolls_Prune_Beyond_MaxRolls()
        {
            string dir = Path.Combine(Path.GetTempPath(), "xrf_logroll_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "test.log");
            // create chain test.log .1 .2
            File.WriteAllText(path, new string('X', 300));
            File.WriteAllText(path + ".1", "old1");
            File.WriteAllText(path + ".2", "old2");
            LogFileRolling.RollIfNeeded(path, maxBytes: 100, maxRolls: 2, log: NullLogger.Instance);
            // After roll: test.log -> new, .1 previous main, .2 former .1 (old .2 deleted)
            File.Exists(path + ".2").Should().BeTrue();
            File.Exists(path + ".3").Should().BeFalse();
        }
    }
}
