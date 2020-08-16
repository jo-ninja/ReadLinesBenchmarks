using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace ReadLinesBenchmarks
{
    [DisassemblyDiagnoser(printSource: true)]
    [MemoryDiagnoser]
    public class ReadLinesBenchmarks
    {
        private static ReadOnlySpan<byte> NewLine => new[] { (byte)'\r', (byte)'\n' };

        private Stream _stream;

        [Params(300_000)]
        public int LineNumber { get; set; }

        [ParamsSource(nameof(LineCharMultiplierValues))]
        public int LineCharMultiplier { get; set; }
        public IEnumerable<int> LineCharMultiplierValues => Enumerable.Range(1, 15).Concat(new[] { 20, 30, 50, 80, 100 });

        [IterationSetup]
        public void IterationSetup()
        {
            _stream = PrepareStream();
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            _stream.Dispose();
        }

        public Stream PrepareStream()
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

            stream.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        [Benchmark(Baseline = true)]
        public async Task<string> ReadLineUsingStringReaderAsync()
        {
            using var sr = new StreamReader(_stream, Encoding.UTF8);
            string str;
            while ((str = await sr.ReadLineAsync()) is not null)
            {
                // simulate string processing
                str = str.AsSpan().Slice(0, 5).ToString();
            }

            return str;
        }

        [Benchmark]
        public async Task<string> ReadLineUsingPipelineAsync()
        {
            var reader = PipeReader.Create(_stream);
            string str;
            while (true)
            {
                ReadResult result = await reader.ReadAsync();
                ReadOnlySequence<byte> buffer = result.Buffer;

                while ((str = ReadLine(ref buffer)) is not null)
                {
                    // simulate string processing
                    str = str.AsSpan().Slice(0, 5).ToString();
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted) break;
            }

            await reader.CompleteAsync();
            return str;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string ReadLine(ref ReadOnlySequence<byte> buffer)
        {
            var reader = new SequenceReader<byte>(buffer);

            if (reader.TryReadTo(out var line, NewLine))
            {
                buffer = buffer.Slice(reader.Position);
                return Encoding.UTF8.GetString(line);
            }

            return default;
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
#if DEBUG
            var summary = BenchmarkRunner.Run<ReadLinesBenchmarks>(new BenchmarkDotNet.Configs.DebugInProcessConfig());
#else
            var summary = BenchmarkRunner.Run<ReadLinesBenchmarks>();
#endif
        }
    }
}
