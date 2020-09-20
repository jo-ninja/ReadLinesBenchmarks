using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Benchmarks
{
    //[DisassemblyDiagnoser(printSource: true)]
    [MemoryDiagnoser]
    public abstract class Base
    {
        protected enum StreamSourceType
        {
            MemoryStream,
            File
        }

        protected StreamSourceType _streamSource => StreamSourceType.MemoryStream;

        protected static ReadOnlySpan<byte> NewLine => new[] { (byte)'\r', (byte)'\n' };

        protected Stream _stream;

        [Params(/*20, */400_000)]
        public int LineNumber { get; set; }

        [ParamsSource(nameof(LineCharMultiplierValues))]
        public int LineCharMultiplier { get; set; }

        public IEnumerable<int> LineCharMultiplierValues => new[] { 15 };
        //public IEnumerable<int> LineCharMultiplierValues => Enumerable.Range(1, 15).Concat(new[] { 20, 30, 50, 80, 100 });

        protected string GetFileName() => $@"{Directory.GetCurrentDirectory()}\Data-{LineNumber}-{LineCharMultiplier}.txt";

        protected Stream PrepareStream()
        {
            var stream = new MemoryStream();

            using var sw = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);
            foreach (var no in Enumerable.Range(1, LineNumber))
            {
                foreach (var _ in Enumerable.Range(1, LineCharMultiplier))
                {
                    sw.Write($"ABC{no:D7}");
                }
                sw.WriteLine();
            }
            sw.Flush();

            return stream;
        }

        protected void InitStream()
        {
            _stream = PrepareStream();

            switch (_streamSource)
            {
                case StreamSourceType.File:
                    {
                        if (File.Exists(GetFileName())) return;

                        using var sw = File.CreateText(GetFileName());
                        _stream.CopyTo(sw.BaseStream);
                        sw.Close();
                        _stream.Dispose();

                        _stream = File.Open(GetFileName(), FileMode.Open, FileAccess.Read, FileShare.Read);
                    }
                    break;

                case StreamSourceType.MemoryStream:
                default:
                    break;
            }
        }

        [GlobalSetup]
        public virtual void GlobalSetup()
        {
            InitStream();
        }

        [IterationSetup]
        public virtual void IterationSetup()
        {
            _stream.Seek(0, SeekOrigin.Begin);
        }

        [IterationCleanup]
        public virtual void IterationCleanup()
        {
        }

        [GlobalCleanup]
        public virtual void GlobalCleanup()
        {
            _stream.Dispose();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            IConfig config = default;
#if DEBUG
            config = new DebugInProcessConfig();
#endif
            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        }
    }
}
